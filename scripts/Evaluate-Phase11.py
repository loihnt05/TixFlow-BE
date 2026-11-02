"""Opt-in live evaluation. Uses an existing Customer JWT; never confirms or purchases."""
import argparse
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://localhost:8080")
    parser.add_argument("--output", default="artifacts/phase11/live-evaluation.json")
    parser.add_argument("--interval", type=float, default=7)
    args = parser.parse_args()
    url = urllib.parse.urlparse(args.base_url)
    if url.scheme != "https" and not (url.scheme == "http" and url.hostname in ("localhost", "127.0.0.1", "::1")):
        parser.error("Use HTTPS or local loopback HTTP.")
    if url.username or url.password or url.query or url.fragment or url.path not in ("", "/"):
        parser.error("Base URL must contain only scheme, host and optional port.")
    token = os.environ.get("TIXFLOW_EVAL_TOKEN")
    if not token:
        parser.error("Set TIXFLOW_EVAL_TOKEN to a current Customer JWT in the environment.")
    root = Path(__file__).resolve().parents[1]
    cases = json.loads((root / "docs/evaluation/phase11-cases.json").read_text(encoding="utf-8"))
    opener = urllib.request.build_opener(NoRedirect())
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
