"""Summarize collected operational evidence, without inventing unsampled peaks.

Usage: python scripts/Analyze-Phase10Monitoring.py <batch>
Includes warm-up and the short post-load tail. Requires no third-party packages.
"""
import json
import re
import statistics
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def records(path):
    # PostgreSQL json_agg can wrap a record across physical lines. Decode a
    # stream of complete JSON values rather than assuming one physical line.
    content = path.read_text(encoding="utf-8-sig")
    decoder = json.JSONDecoder()
    offset = 0
    while offset < len(content):
        if content[offset].isspace():
            offset += 1
            continue
        value, offset = decoder.raw_decode(content, offset)
        yield value


def describe(values):
    return {"samples": len(values), "min": min(values), "median": statistics.median(values),
            "max": max(values), "first": values[0], "last": values[-1]}


def memory(value):
    match = re.fullmatch(r"([\d.]+)([KMGT]?i?B)", value)
    if not match:
        raise ValueError(value)
    number, unit = match.groups()
    return float(number) * (1024 if "i" in unit else 1000) ** ("KMGT".index(unit[0]) + 1 if unit != "B" else 0)


def main(root):
    output = []
    for folder in sorted(root.iterdir()):
        if not folder.is_dir() or not re.fullmatch(r"(baseline|optimized)-\d+", folder.name):
            continue
        manifest = read(folder / "manifest.json")
        database = defaultdict(list)
        resources = defaultdict(lambda: defaultdict(list))
        queues = defaultdict(lambda: defaultdict(list))
        blockers = defaultdict(int)
        times = []
        for point in records(folder / "database.jsonl"):
            times.append(datetime.fromisoformat(point["utc"]).timestamp())
            for key, value in point.items():
                if isinstance(value, (int, float)):
                    database[key].append(value)
            for query in point["blocked_queries"]:
                blockers[query["query"]] += 1
        for point in records(folder / "resources.jsonl"):
            for container in point["containers"]:
                service = re.sub(r"^tixflow-load-\w+-", "", container["Name"])
                service = re.sub(r"-run-.*$|-\d+$", "", service)
                resources[service]["cpu_percent"].append(float(container["CPUPerc"].rstrip("%")))
                resources[service]["memory_bytes"].append(memory(container["MemUsage"].split(" / ")[0]))
        for point in records(folder / "queue-samples.jsonl"):
            for queue in point["queues"]:
                for field in ("messages_ready", "messages_unacknowledged"):
                    if queue.get(field) is not None:
                        queues[queue["name"]][field].append(queue[field])
        output.append({
            "runId": manifest["runId"], "variant": manifest["variant"], "profile": manifest["profile"],
            "result": read(folder / "result.json"),
            "database": {k: describe(v) for k, v in database.items()},
            "resources": {s: {k: describe(v) for k, v in m.items()} for s, m in resources.items()},
            "queues": {s: {k: describe(v) for k, v in m.items()} for s, m in queues.items()},
            "sample_gap_seconds": describe([b-a for a, b in zip(times, times[1:])]) if len(times) > 1 else None,
            "blocked_query_samples": dict(sorted(blockers.items(), key=lambda item: -item[1])),
            "otherContainers": manifest.get("otherContainers", []),
        })
    (root / "monitoring-analysis.json").write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(f"Analyzed {len(output)} runs; maxima are sampled, not continuous.")


if __name__ == "__main__":
    main(Path(sys.argv[1]).resolve())
