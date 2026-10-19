"""Aggregate measured custom k6 points; exclude setup and preserve every run ID.

Usage: python scripts/Summarize-Phase10.py artifacts/phase10/<batch>
Requires only Python's standard library. Raw input stays in the ignored artifact folder.
"""
import csv
import gzip
import json
import statistics
import sys
from collections import defaultdict
from datetime import datetime
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def percentile(values, p):
    values = sorted(values)
    index = (len(values) - 1) * p
    lower = int(index)
    return values[lower] + (values[min(lower + 1, len(values) - 1)] - values[lower]) * (index - lower)


def summarize(root):
    rows = []
    runs = []
    for folder in sorted(root.iterdir()):
        if not folder.is_dir() or folder.name == "private":
            continue
        if not (folder / "result.json").exists():
            raise ValueError(f"Incomplete run: {folder.name}")
        result = read(folder / "result.json")
        if not result["passed"]:
            raise ValueError(f"Failed run must not enter a passing comparison: {folder.name}")
        manifest = read(folder / "manifest.json")
        groups = defaultdict(list)
        errors = defaultdict(int)
        conflicts = defaultdict(int)
        rejects = defaultdict(int)
        timestamps = []
        with gzip.open(folder / "raw.json.gz", "rt", encoding="utf-8") as source:
            for line in source:
                point = json.loads(line)
                if point["type"] != "Point":
                    continue
                data = point["data"]
                endpoint = data.get("tags", {}).get("endpoint")
                if not endpoint:
                    continue
                metric = point["metric"]
                if metric == "endpoint_latency":
                    groups[endpoint].append(data["value"])
                    timestamps.append(datetime.fromisoformat(data["time"].replace("Z", "+00:00")).timestamp())
                elif metric == "technical_errors":
                    errors[endpoint] += data["value"]
                elif metric == "business_conflicts":
                    conflicts[endpoint] += data["value"]
                elif metric == "admission_rejections":
                    rejects[endpoint] += data["value"]
        if not timestamps:
            raise ValueError(f"No measured HTTP points: {folder.name}")
        seconds = max(timestamps) - min(timestamps)
        for endpoint, values in sorted(groups.items()):
            rows.append(dict(run_id=manifest["runId"], variant=manifest["variant"], profile=manifest["profile"],
                endpoint=endpoint, requests=len(values), measured_seconds=round(seconds, 3),
                requests_per_second=round(len(values) / seconds, 3),
                p50_ms=round(percentile(values, .5), 3), p95_ms=round(percentile(values, .95), 3), p99_ms=round(percentile(values, .99), 3),
                technical_errors=int(errors[endpoint]), business_conflicts=int(conflicts[endpoint]), admission_rejections=int(rejects[endpoint])))
        runs.append(dict(run_id=manifest["runId"], variant=manifest["variant"], profile=manifest["profile"],
            measured_seconds=seconds, requests=sum(map(len, groups.values())), invariants=read(folder / "invariants.json")))
    if not rows:
        raise ValueError("No completed runs found")
    with (root / "endpoints.csv").open("w", newline="", encoding="utf-8") as target:
        writer = csv.DictWriter(target, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    aggregate = []
    for variant, endpoint in sorted({(r["variant"], r["endpoint"]) for r in rows}):
        selected = [r for r in rows if r["variant"] == variant and r["endpoint"] == endpoint]
        entry = dict(variant=variant, endpoint=endpoint, repetitions=len(selected))
        for metric in ("p50_ms", "p95_ms", "p99_ms", "requests_per_second"):
            values = [r[metric] for r in selected]
            entry[metric] = dict(median=statistics.median(values), min=min(values), max=max(values),
                stdev=statistics.stdev(values) if len(values) > 1 else 0)
        aggregate.append(entry)
    (root / "aggregate.json").write_text(json.dumps(dict(runs=runs, endpoints=aggregate), indent=2), encoding="utf-8")
    lines = ["# Phase 10 HTTP load results", "", f"Completed runs: {len(runs)}.", "",
        "Each endpoint rate uses the complete measured workload window (first to last custom HTTP sample), excluding setup. Concurrent scenarios share CPU and database locks; this is a mixed-workload experiment, not isolated endpoint capacity.", "",
        "| Variant | Endpoint | Runs | Median p50 ms | Median p95 ms | p95 min–max ms | Median p99 ms | Median req/s |", "|---|---|---:|---:|---:|---:|---:|---:|"]
    for r in aggregate:
        lines.append(f"| {r['variant']} | {r['endpoint']} | {r['repetitions']} | {r['p50_ms']['median']:.2f} | {r['p95_ms']['median']:.2f} | {r['p95_ms']['min']:.2f}–{r['p95_ms']['max']:.2f} | {r['p99_ms']['median']:.2f} | {r['requests_per_second']['median']:.2f} |")
    lines += ["", "All included runs passed k6 thresholds and database invariants. Zero invariant violations is required but does not establish production capacity. See manifests, raw points, resource/database samples and the phase 10 runbook for limitations."]
    (root / "comparison.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Summarized {len(runs)} runs into {root}")


if __name__ == "__main__":
    summarize(Path(sys.argv[1]).resolve())
