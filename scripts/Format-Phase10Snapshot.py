"""Create a presentation copy of a Grafana snapshot; numeric points stay intact."""
import json
import sys
from datetime import datetime, timezone
from pathlib import Path


def format_snapshot(source, target):
    payload = json.loads(source.read_text(encoding="utf-8-sig"))
    dashboard = payload["dashboard"]
    dashboard["refresh"] = ""
    timestamps = []
    for panel in dashboard["panels"]:
        defaults = panel["fieldConfig"]["defaults"]
        if defaults.get("unit") == "percentunit":
            defaults.update(min=0, max=1)
        for frame in panel["snapshotData"]:
            metric = json.loads(frame["name"])
            label = metric.get("endpoint") or metric.get("service") or metric.get("queue") or metric.get("__name__", panel["title"]).removeprefix("tixflow_")
            if "scenario" in metric and "endpoint" in metric:
                label += " [" + metric["scenario"] + "]"
            if "queue" in metric:
                label += " / " + metric["__name__"].removeprefix("tixflow_queue_")
            frame["name"] = label
            for field in frame["fields"]:
                if field["type"] == "time":
                    timestamps.extend(field["values"])
                else:
                    field["name"] = label
                    if defaults.get("unit") == "percentunit":
                        field.setdefault("config", {}).update(min=0, max=1)
    if timestamps:
        dashboard["time"] = {key:datetime.fromtimestamp(value/1000, timezone.utc).isoformat()
                             for key, value in (("from",min(timestamps)),("to",max(timestamps)))}
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    original = json.loads(source.read_text(encoding="utf-8-sig"))
    for before, after in zip(original["dashboard"]["panels"], dashboard["panels"], strict=True):
        for a, b in zip(before["snapshotData"],after["snapshotData"],strict=True):
            for old, new in zip(a["fields"],b["fields"],strict=True):
                if old["values"] != new["values"]:
                    raise ValueError("Presentation formatting changed numeric points")


if __name__ == "__main__":
    format_snapshot(Path(sys.argv[1]),Path(sys.argv[2]))
