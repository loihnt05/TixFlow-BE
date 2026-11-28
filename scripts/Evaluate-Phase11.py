"""Opt-in live evaluation. Uses an existing Customer JWT; never confirms or purchases."""
import argparse
from datetime import date, datetime, timezone
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def preflight_fixture(base_url, cases, opener, now):
    """Verify public catalog data before spending provider calls on fixed cases."""
    base = base_url.rstrip("/")
    events = {}
    for case in cases:
        fixture = case.get("fixture", {})
        for value in fixture.values():
            if str(value) not in case["message"]:
                raise ValueError(f"{case['id']}: fixture metadata differs from the message")

        if "dateFrom" in fixture:
            start = date.fromisoformat(fixture["dateFrom"])
            end = date.fromisoformat(fixture["dateTo"])
            if end < start or end < now.date():
                raise ValueError(f"{case['id']}: search date range has expired")
            query = urllib.parse.urlencode({"dateFrom": start.isoformat(), "dateTo": end.isoformat(), "limit": 100})
            with opener.open(f"{base}/api/v1/events?{query}", timeout=15) as response:
                page = json.load(response)
            if not any(
                session["status"] == "OnSale" and parse_utc(session["startsAtUtc"]) > now
                and start <= parse_utc(session["startsAtUtc"]).date() <= end
                for event in page["events"] for session in event["sessions"]
            ):
                raise ValueError(f"{case['id']}: no future on-sale event in the requested range")

        event_id = fixture.get("eventId")
        if not event_id:
            continue
        if event_id not in events:
            with opener.open(f"{base}/api/v1/events/{event_id}", timeout=15) as response:
                events[event_id] = json.load(response)
        event = events[event_id]
        if event.get("id") != event_id or event.get("status") != "Published":
            raise ValueError(f"{case['id']}: referenced event is missing or unpublished")
        if "sessionId" not in fixture:
            continue
        session = next((item for item in event["sessions"] if item["id"] == fixture["sessionId"]), None)
        if session is None or session["status"] != "OnSale" or parse_utc(session["startsAtUtc"]) <= now:
            raise ValueError(f"{case['id']}: referenced session is unavailable or in the past")
        if not in_sale(event, now):
            raise ValueError(f"{case['id']}: referenced event is outside its sale window")
        if "ticketTypeId" not in fixture:
            continue
        ticket = next((item for item in session["ticketTypes"] if item["id"] == fixture["ticketTypeId"]), None)
        if ticket is None or not in_sale(ticket, now) or ticket["availableQuantity"] < 1:
            raise ValueError(f"{case['id']}: referenced ticket type is missing, closed, or sold out")


def parse_utc(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)


def in_sale(item, now):
    start = item.get("saleStartsAtUtc")
    end = item.get("saleEndsAtUtc")
    return (not start or parse_utc(start) <= now) and (not end or parse_utc(end) > now)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://localhost:8080")
    parser.add_argument("--output", default="artifacts/phase11/live-evaluation.json")
    parser.add_argument("--interval", type=float, default=7)
    parser.add_argument("--preflight-only", action="store_true", help="Check public fixture without a JWT or provider calls")
    args = parser.parse_args()
    url = urllib.parse.urlparse(args.base_url)
    if url.scheme != "https" and not (url.scheme == "http" and url.hostname in ("localhost", "127.0.0.1", "::1")):
        parser.error("Use HTTPS or local loopback HTTP.")
    if url.username or url.password or url.query or url.fragment or url.path not in ("", "/"):
        parser.error("Base URL must contain only scheme, host and optional port.")
    root = Path(__file__).resolve().parents[1]
    cases = json.loads((root / "docs/evaluation/phase11-cases.json").read_text(encoding="utf-8"))
    opener = urllib.request.build_opener(NoRedirect())
    try:
        preflight_fixture(args.base_url, cases, opener, datetime.now(timezone.utc))
    except (ValueError, KeyError, urllib.error.URLError, TimeoutError) as exc:
        parser.error(f"Fixture preflight failed: {exc}")
    print("Fixture preflight passed")
    if args.preflight_only:
        return
    token = os.environ.get("TIXFLOW_EVAL_TOKEN")
    if not token:
        parser.error("Set TIXFLOW_EVAL_TOKEN to a current Customer JWT in the environment.")
    with opener.open(args.base_url.rstrip("/") + "/api/v1/assistant/status", timeout=15) as response:
        status = json.load(response)
    if status.get("status") != "ready":
        parser.error("Provider is not configured; do not score fallback as an AI evaluation.")
    rows = []
    for case in cases:
        started = time.monotonic()
        request = urllib.request.Request(args.base_url.rstrip("/") + "/api/v1/assistant/messages",
            data=json.dumps({"message": case["message"]}).encode(),
            headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"}, method="POST")
        try:
            with opener.open(request, timeout=45) as response:
                reply = json.load(response)
            used = reply.get("toolsUsed", [])
            expected = case["expectedAnyTool"]
            rows.append({"id": case["id"], "latencyMs": round((time.monotonic() - started) * 1000),
                "fallback": reply.get("fallback"), "toolsUsed": used,
                "toolSelectionHeuristic": not expected or bool(set(expected) & set(used)),
                "proposalPolicyPassed": case["allowProposal"] or not reply.get("proposals"),
                "answerForManualReview": reply.get("message"), "manualCorrectness": None})
        except (urllib.error.URLError, TimeoutError):
            rows.append({"id": case["id"], "error": "HTTP_OR_TIMEOUT"})
        print(case["id"], "recorded")
        time.sleep(max(0, args.interval))
    latencies = sorted(row["latencyMs"] for row in rows if "latencyMs" in row)
    report = {"provider": status["provider"], "model": status["model"], "cases": len(rows),
        "errorsOrFallbacks": sum(bool(row.get("error") or row.get("fallback")) for row in rows),
        "p95Ms": latencies[min(len(latencies)-1, int(len(latencies)*0.95))] if latencies else None,
        "semanticEvaluationComplete": False, "notes": "Manual correctness review required. No confirm-hold calls were made.", "results": rows}
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Report:", output)


if __name__ == "__main__":
    main()
