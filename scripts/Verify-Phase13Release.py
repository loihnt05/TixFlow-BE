"""Read-only phase 13 audit. Works on the workspace or a review ZIP, with no Git,
Docker, network, extraction, or execution of packaged code. Python standard library.
Integrity is relative to supplied hashes; it is not a digital signature.
"""
import argparse
import hashlib
import importlib.util
import io
import json
import math
from pathlib import Path, PurePosixPath
import posixpath
import re
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("evidence", Path(__file__).with_name("Build-Phase13Evidence.py"))
evidence = importlib.util.module_from_spec(spec)
spec.loader.exec_module(evidence)


class Reader:
    def __init__(self, root, package):
        self.root = root
        self.archive = zipfile.ZipFile(package) if package else None
        self.prefix = "tixflow-backend/"

    def read(self, name):
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name:
            raise ValueError(f"Unsafe evidence reference: {name}")
        return self.archive.read(self.prefix + name) if self.archive else (self.root / name).read_bytes()

    def json(self, name):
        return json.loads(self.read(name).decode("utf-8-sig"))

    def names(self, folder):
        if self.archive:
            return [n[len(self.prefix):] for n in self.archive.namelist() if n.startswith(self.prefix + folder)]
        return [p.relative_to(self.root).as_posix() for p in (self.root / folder).rglob("*") if p.is_file()]

    def close(self):
        if self.archive:
            self.archive.close()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def audit(reader):
    checks = []
    summary = reader.json("docs/release/evidence-summary.json")
    paths = [item["path"] for item in summary["inputs"]]
    require(len(paths) == len(set(paths)), "Duplicate input evidence records")
    for item in summary["inputs"]:
        body = reader.read(item["path"])
        require(len(body) == item["bytes"] and hashlib.sha256(body).hexdigest() == item["sha256"], f"Evidence hash mismatch: {item['path']}")
    checks.append({"check": "evidence_hashes", "passed": True, "files": len(paths)})

    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    trx = ET.fromstring(reader.read("artifacts/phase13/verification/phase13-regression.trx"))
    counters = trx.find(".//t:Counters", ns).attrib
    counts = {k: int(counters[k]) for k in ("total", "executed", "passed", "failed", "notExecuted")}
    evidence.validate_regression(counts)
    require(counts == summary["regression"], "TRX counters differ from reported regression")
    results = trx.findall(".//t:UnitTestResult", ns)
    require(len(results) == counts["total"] and all(r.attrib["outcome"] == "Passed" for r in results), "TRX result entries disagree with counters")
    checks.append({"check": "regression_trx", "passed": True, "counts": counts})

    statuses = []
    for profile in summary["profiles"]:
        run_paths = sorted(n for n in reader.names(profile["batch"] + "/") if re.fullmatch(re.escape(profile["batch"]) + r"/(baseline|optimized)-\d+/result.json", n))
        names = [PurePosixPath(n).parent.name for n in run_paths]
        actual = [reader.json(n) for n in run_paths]
        complete = evidence.complete_runs(names, actual, summary["campaign_status"]["repetitions"])
        require(complete == profile["complete"] and len(actual) == profile["observed_runs"] and sum(r.get("passed") is True for r in actual) == profile["passing_runs"], f"Incorrect profile completion claim: {profile['profile']}")
        for run in names:
            folder = f"{profile['batch']}/{run}"
            manifest = reader.json(folder + "/manifest.json")
            require(manifest["profile"] == profile["profile"] and manifest["variant"] == run.split("-")[0], "Run identity mismatch")
            for source in manifest["inputs"]:
                copied = reader.read(folder + "/inputs/" + source["path"].replace("\\", "/"))
                require(hashlib.sha256(copied).hexdigest() == source["sha256"], "Historical harness hash mismatch")
        if complete:
            require(reader.json(profile["batch"] + "/aggregate.json") == profile["aggregate"], "Embedded aggregate differs from source")
        statuses.append({"profile": profile["profile"], "complete": complete, "passed_runs": profile["passing_runs"], "observed_runs": len(names)})
    require(summary["campaign_status"] == reader.json("artifacts/phase10/campaign-20261022T131923523Z/status.json"), "Campaign status differs from evidence")
    require(not summary["release_ready"], "This audit does not certify production release readiness")
    checks.append({"check": "campaign_reporting", "passed": True, "profiles": statuses})

    api = reader.json("docs/release/openapi.json")
    api_provenance = reader.json("docs/release/openapi-provenance.json")
    require(len(api["paths"]) == api_provenance["paths"], "OpenAPI path count mismatch")
    for path in ("/api/v1/holds", "/api/v1/holds/{holdId}/checkout", "/api/v1/tickets/check-ins", "/api/v1/assistant/confirm-hold"):
        require(path in api["paths"], f"Required API route missing: {path}")
    checks.append({"check": "openapi", "passed": True, "paths": len(api["paths"])})

    documents = [n for n in reader.names("docs/release/") if n.endswith(".md")] + ["docs/phase-13-handover.md"]
    link_count = 0
    for name in documents:
        for link in re.findall(r"\]\(([^)]+)\)", reader.read(name).decode("utf-8")):
            if link.startswith(("http:", "https:", "#")):
                continue
            target = posixpath.normpath(posixpath.join(posixpath.dirname(name), link.split("#")[0]))
            reader.read(target)
            link_count += 1
    checks.append({"check": "document_links", "passed": True, "local_links": link_count})

    chart_ns = {"c": "http://schemas.openxmlformats.org/drawingml/2006/chart"}
    ramp = next(p for p in summary["profiles"] if p["profile"] == "ramp")["aggregate"]["endpoints"]
    expected = [
        [[r[v]["p95_ms"] / 1000 for r in summary["phase9"]] for v in ("baseline", "optimized")],
        [[next(r for r in ramp if r["variant"] == v and r["endpoint"] == endpoint)["p95_ms"]["median"] for endpoint in ("browse", "hold", "checkout", "callback")] for v in ("baseline", "optimized")]]
    with zipfile.ZipFile(io.BytesIO(reader.read("docs/release/TixFlow-defense.pptx"))) as deck:
        slides = [n for n in deck.namelist() if re.fullmatch(r"ppt/slides/slide\d+.xml", n)]
        notes = [n for n in deck.namelist() if re.fullmatch(r"ppt/notesSlides/notesSlide\d+.xml", n)]
        charts = sorted(n for n in deck.namelist() if re.fullmatch(r"ppt/(?:slides/)?charts/chart\d+.xml", n))
        require(len(slides) == len(notes) == 11 and len(charts) == 2, "Slide/notes/chart count mismatch")
        for name, expected_series in zip(charts, expected):
            xml = ET.fromstring(deck.read(name))
            series = xml.findall(".//c:barChart/c:ser", chart_ns)
            require(len(series) == 2, "Chart series count mismatch")
            for actual, wanted in zip(series, expected_series):
                values = [float(v.text) for v in actual.findall(".//c:val//c:pt/c:v", chart_ns)]
                require(len(values) == len(wanted) and all(math.isclose(a, b, rel_tol=1e-9, abs_tol=1e-6) for a, b in zip(values, wanted)), f"Slide chart disagrees with measured evidence: {name}")
        require(len([n for n in deck.namelist() if n.startswith("ppt/embeddings/") and n.endswith(".xlsx")]) == 2, "Editable chart workbooks missing")
    checks.append({"check": "slide_numeric_provenance", "passed": True, "slides": 11, "native_charts": 2})
    browser = reader.json("docs/release/offline-demo-check.json")
    require(browser["passed"] is True and hashlib.sha256(reader.read(browser["input"])).hexdigest() == browser["sha256"], "Offline browser evidence is stale or failed")
    checks.append({"check": "offline_browser_evidence", "passed": True, "recorded_at": browser["checkedUtc"], "scope": browser["scope"]})
    video = reader.json("docs/release/fallback-video.json")
    require(hashlib.sha256(reader.read(video["output"])).hexdigest() == video["sha256"] and video["playback"]["decoded"] is True, "Fallback video differs from verified playback")
    require(video["htmlSha256"] == browser["sha256"], "Fallback video HTML provenance differs")
    for frame in video["frames"]:
        require(hashlib.sha256(reader.read(frame["path"])).hexdigest() == frame["sha256"], "Video frame hash mismatch")
    checks.append({"check": "fallback_video_evidence", "passed": True, "scope": video["scope"]})
    return {"checked_utc": datetime.now(timezone.utc).isoformat(), "review_checks_passed": True,
            "release_ready": False, "checks": checks, "open_gates": summary["open_gates"],
            "scope": "File integrity and consistency; not fresh application tests, browser/VPS acceptance, real AI quality or independent reproduction."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--package", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.package:
        evidence.verify(args.package)
    reader = Reader(args.root, args.package)
    try:
        result = audit(reader)
    finally:
        reader.close()
    if args.output:
        evidence.write_json(args.output, result)
    print(json.dumps(result, ensure_ascii=True, indent=2))
