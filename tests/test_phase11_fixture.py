"""Offline checks for the live AI evaluation fixture gate."""
from datetime import datetime, timezone
import importlib.util
import io
import json
from pathlib import Path
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts/Evaluate-Phase11.py"
spec = importlib.util.spec_from_file_location("phase11_evaluation", SCRIPT)
evaluation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evaluation)
NOW = datetime(2026, 11, 27, tzinfo=timezone.utc)
EVENT_ID = "30000000-0000-0000-0000-000000000001"
SESSION_ID = "40000000-0000-0000-0000-000000000001"
TICKET_ID = "50000000-0000-0000-0000-000000000012"


class Response(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


class Catalog:
    def __init__(self, event):
        self.event = event

    def open(self, url, timeout):
        if "?" in url:
            return Response(json.dumps({"events": [self.event]}).encode())
        return Response(json.dumps(self.event).encode())


def fixture_event(sale_end="2026-12-20T11:00:00Z"):
    return {"id": EVENT_ID, "status": "Published",
            "saleStartsAtUtc": "2026-09-01T00:00:00Z", "saleEndsAtUtc": sale_end,
            "sessions": [{"id": SESSION_ID, "status": "OnSale",
                          "startsAtUtc": "2026-12-20T12:00:00Z",
                          "ticketTypes": [{"id": TICKET_ID, "availableQuantity": 50,
                                           "saleStartsAtUtc": None, "saleEndsAtUtc": None}]}]}


CASES = [
    {"id": "search-04", "message": "2026-12-01 to 2026-12-20",
     "fixture": {"dateFrom": "2026-12-01", "dateTo": "2026-12-20"}},
    {"id": "prepare-01", "message": f"{EVENT_ID} {SESSION_ID} {TICKET_ID}",
     "fixture": {"eventId": EVENT_ID, "sessionId": SESSION_ID, "ticketTypeId": TICKET_ID}},
]


class FixturePreflightTests(unittest.TestCase):
    def test_valid_fixture_passes(self):
        evaluation.preflight_fixture("http://localhost:8080", CASES, Catalog(fixture_event()), NOW)

    def test_expired_search_range_stops_evaluation(self):
        with self.assertRaisesRegex(ValueError, "search date range has expired"):
            evaluation.preflight_fixture("http://localhost:8080", CASES, Catalog(fixture_event()),
                                         datetime(2026, 12, 21, tzinfo=timezone.utc))

    def test_closed_sale_stops_evaluation(self):
        with self.assertRaisesRegex(ValueError, "outside its sale window"):
            evaluation.preflight_fixture("http://localhost:8080", CASES,
                                         Catalog(fixture_event("2026-11-26T00:00:00Z")), NOW)


if __name__ == "__main__":
    unittest.main()
