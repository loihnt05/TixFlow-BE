"""Build review evidence from measured inputs, then optionally archive both repositories.

Python 3.12+, matplotlib (charts only). No services started, no deployment performed.
Run from any directory. Raw campaign artifacts must already exist.
"""
import argparse
import base64
import hashlib
import importlib.util
import json
import statistics
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs/release"
CAMPAIGN = ROOT / "artifacts/phase10/campaign-20261022T131923523Z/status.json"
TRX = ROOT / "artifacts/phase13/verification/phase13-regression.trx"
INPUTS = set()


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def read(path):
    INPUTS.add(path)
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def git(repo, *args):
    return subprocess.check_output(["git", "-C", str(repo), *args]).decode("utf-8").strip()


def revision(repo):
    return {"head": git(repo, "rev-parse", "HEAD"),
            "dirty": bool(git(repo, "status", "--porcelain")),
            "identity": "HEAD plus file hashes in package; not a clean release commit"}


def complete_runs(names, results, repetitions):
    expected = {f"{variant}-{i}" for variant in ("baseline", "optimized") for i in range(1, repetitions + 1)}
    return repetitions > 0 and set(names) == expected and len(names) == len(expected) == len(results) and all(r.get("passed") is True for r in results)


def validate_regression(counters):
    if counters["total"] < 1 or counters["total"] != counters["executed"] or counters["total"] != counters["passed"] or counters["failed"] or counters["notExecuted"]:
        raise ValueError("Regression must contain executed, passing tests with no failure or skip")


def build():
    INPUTS.clear()
    OUT.mkdir(parents=True, exist_ok=True)
    baseline = read(ROOT / "docs/reports/optimized-v1/reference-concurrency.json")
    optimized = read(ROOT / "docs/reports/optimized-v1/concurrency.json")
    bursts = []
    for n in sorted({r["requests"] for r in baseline["results"]}):
        row = {"requests_per_burst": n}
        for name, data in (("baseline", baseline), ("optimized", optimized)):
            runs = [r for r in data["results"] if r["requests"] == n]
            if len(runs) != 2 or not all(r["oversold"] == 0 and r["completed"] == n for r in runs):
                raise ValueError(f"Invalid phase 9 burst evidence: {name}/{n}")
            row[name] = {"repetitions": len(runs), "p95_ms": statistics.median(r["p95Ms"] for r in runs),
                         "rps": statistics.median(r["throughput"] for r in runs)}
        bursts.append(row)
    spec = importlib.util.spec_from_file_location("phase10", ROOT / "scripts/Summarize-Phase10.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    campaign = read(CAMPAIGN)
    profiles = []
    for batch in campaign["batches"]:
        # Original manifests use Windows paths; keep this rebuild portable.
        folder = ROOT / "artifacts/phase10" / batch["path"].replace("\\", "/").split("/")[-1]
        runs = sorted(p for p in folder.iterdir() if p.is_dir() and p.name.startswith(("baseline-", "optimized-")))
        results = [read(p / "result.json") for p in runs]
        passing = complete_runs([p.name for p in runs], results, campaign["repetitions"])
        entry = {"profile": batch["profile"], "vus_parameter": batch["vus"],
                 "batch": str(folder.relative_to(ROOT)).replace("\\", "/"),
                 "observed_runs": len(runs), "passing_runs": sum(r.get("passed") is True for r in results),
                 "complete": passing, "results": results}
        for run in runs:
            manifest = read(run / "manifest.json")
            if manifest["variant"] != run.name.split("-")[0] or manifest["profile"] != batch["profile"]:
                raise ValueError(f"Manifest variant/profile mismatch: {run.name}")
            for source in manifest["inputs"]:
                relative = PurePosixPath(source["path"].replace("\\", "/"))
                if relative.is_absolute() or ".." in relative.parts or ":" in str(relative):
                    raise ValueError("Unsafe harness source path")
                copied = run / "inputs" / relative
                if not copied.is_file() or digest(copied) != source["sha256"]:
                    raise ValueError(f"Harness snapshot mismatch: {run.name}/{relative}")
                INPUTS.add(copied)
            INPUTS.update(p for p in run.iterdir() if p.name in {
                "raw.json.gz", "manifest.json", "invariants.json", "result.json", "summary.json",
                "database.jsonl", "resources.jsonl", "queue-samples.jsonl", "queries.json",
                "prometheus-range.json", "grafana-snapshot.json", "monitoring-result.json", "images.json", "queues.json"})
        if passing:
            module.summarize(folder)
            entry["aggregate"] = read(folder / "aggregate.json")
            for name in ("endpoints.csv", "comparison.md"):
                INPUTS.add(folder / name)
        profiles.append(entry)
    INPUTS.add(TRX)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counters = ET.parse(TRX).find(".//t:Counters", ns).attrib
    regression = {k: int(counters[k]) for k in ("total", "executed", "passed", "failed", "notExecuted")}
    validate_regression(regression)
    deployment = read(ROOT / "artifacts/phase12/deployment-integration.json")
    for name in ("phase12-final.trx", "backend-audit.json", "frontend-audit.json"):
        path = ROOT / "artifacts/phase12" / name
        if path.exists():
            INPUTS.add(path)
    migrations = [ROOT / "database/init/001_initial_schema.sql", *sorted((ROOT / "database/migrations").glob("*.sql"))]
    INPUTS.update(migrations)
    for name in ("docs/release/offline-demo-check.json", "docs/release/fallback-video.json", "docs/release/TixFlow-evidence-fallback.webm"):
        path = ROOT / name
        if path.exists():
            INPUTS.add(path)
    video_metadata = ROOT / "docs/release/fallback-video.json"
    if video_metadata.exists():
        for frame in read(video_metadata)["frames"]:
            relative = PurePosixPath(frame["path"])
            if relative.is_absolute() or ".." in relative.parts or ":" in str(relative):
                raise ValueError("Unsafe video frame path")
            path = ROOT / relative
            if not path.is_file() or digest(path) != frame["sha256"]:
                raise ValueError(f"Video source frame mismatch: {relative}")
            INPUTS.add(path)
    data = {"generated_utc": datetime.now(timezone.utc).isoformat(), "status": "review-candidate",
            "release_ready": False, "repositories": {p.name: revision(p) for p in (ROOT, ROOT.parent / "tixflow-frontend")},
            "regression": regression, "phase9": bursts, "campaign_status": campaign,
            "profiles": profiles, "deployment_fixture": deployment,
            "migrations": [{"path": p.relative_to(ROOT).as_posix(), "sha256": digest(p)} for p in migrations],
            "open_gates": ["phase10 soak failed; complete comparable campaign and review latency tails",
                           "real-provider AI evaluation and authenticated browser acceptance",
                           "VPS DNS/TLS/backup/restore/redeploy acceptance deferred by user",
                           "clean commit pair and published immutable application image digests",
                           "independent clean-machine reproduction and actual demo recording"],
            "inputs": [{"path": p.relative_to(ROOT).as_posix(), "bytes": p.stat().st_size, "sha256": digest(p)}
                       for p in sorted(INPUTS)]}
    write_json(OUT / "evidence-summary.json", data)
    table = ["# Số liệu đã đối chiếu", "", "Sinh từ raw bằng `scripts/Build-Phase13Evidence.py`. Median của các lần chạy; không gộp percentile giữa các workload.", "",
             "## Phase 9 — TestServer, hai lần lặp", "", "| Requests/burst | Baseline req/s | Optimized req/s | Baseline p95 ms | Optimized p95 ms |", "|---:|---:|---:|---:|---:|"]
    for r in bursts:
        b, o = r["baseline"], r["optimized"]
        table.append(f"| {r['requests_per_burst']} | {b['rps']:.2f} | {o['rps']:.2f} | {b['p95_ms']:.2f} | {o['p95_ms']:.2f} |")
    for profile in profiles:
        table += ["", f"## Phase 10 — {profile['profile']}", "", f"{profile['passing_runs']}/{profile['observed_runs']} run quan sát đạt; complete={profile['complete']}. Nguồn: `{profile['batch']}`."]
        if not profile["complete"]:
            table += ["", "Không đưa run lỗi vào so sánh đạt. Chiến dịch vẫn failed; chưa có kết quả soak hoàn chỉnh."]
            continue
        table += ["", "| Variant | Endpoint | Runs | p50 ms | p95 ms | p99 ms | req/s |", "|---|---|---:|---:|---:|---:|---:|"]
        for r in profile["aggregate"]["endpoints"]:
            table.append(f"| {r['variant']} | {r['endpoint']} | {r['repetitions']} | " + " | ".join(f"{r[m]['median']:.2f}" for m in ("p50_ms", "p95_ms", "p99_ms", "requests_per_second")) + " |")
    (OUT / "measured-results.md").write_text("\n".join(table) + "\n", encoding="utf-8")
    charts(data)
    fallback(data)
    return data


def charts(data):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 11})
    figures = OUT / "figures"
    figures.mkdir(exist_ok=True)
    colors = ("#64748b", "#0f766e")
    fig, ax = plt.subplots(figsize=(9, 4.8), layout="constrained")
    for offset, variant, color in zip((-.18, .18), ("baseline", "optimized"), colors):
        bars = ax.bar([i + offset for i in range(3)], [r[variant]["p95_ms"] / 1000 for r in data["phase9"]], .36, label=variant, color=color)
        ax.bar_label(bars, fmt="%.2f", padding=3)
    ax.set(xticks=range(3), xticklabels=[str(r["requests_per_burst"]) for r in data["phase9"]], xlabel="Requests per burst", ylabel="Median p95 (seconds)", title="Phase 9: hold latency on TestServer (2 repetitions)", ylim=(0, 32))
    ax.legend(frameon=False)
    ax.spines[["top", "right"]].set_visible(False)
    fig.savefig(figures / "phase9-p95.png", dpi=170)
    plt.close(fig)
    ramp = next(p for p in data["profiles"] if p["profile"] == "ramp")["aggregate"]
    names = ["browse", "hold", "checkout", "callback"]
    fig, axes = plt.subplots(1, 2, figsize=(12, 5), layout="constrained")
    for ax, metric in zip(axes, ("p95_ms", "p99_ms")):
        for offset, variant, color in zip((-.18, .18), ("baseline", "optimized"), colors):
            values = [next(r for r in ramp["endpoints"] if r["variant"] == variant and r["endpoint"] == name)[metric]["median"] for name in names]
            ax.bar([i + offset for i in range(len(names))], values, .36, label=variant, color=color)
        ax.set(xticks=range(len(names)), xticklabels=names, ylabel="Milliseconds", title=f"Median {metric.replace('_ms', '')} across 3 runs")
        ax.spines[["top", "right"]].set_visible(False)
        ax.legend(frameon=False)
    fig.suptitle("Phase 10 ramp: 20 VU mixed workload; selected endpoints")
    fig.savefig(figures / "phase10-ramp-tails.png", dpi=170)
    plt.close(fig)


def fallback(data):
    # Offline evidence walkthrough, explicitly not a recorded application demo.
    images = [base64.b64encode((OUT / "figures" / name).read_bytes()).decode() for name in ("phase9-p95.png", "phase10-ramp-tails.png")]
    sections = ["<h1>TixFlow</h1><h2>Bản trình bày bằng chứng offline</h2><p>Review candidate · 24/10/2026</p><p>Đây là tài liệu dự phòng từ kết quả đã đo, không phải video thao tác ứng dụng hoặc bằng chứng VPS.</p>",
                "<h1>Luồng nghiệp vụ</h1><p>Đăng nhập → chọn vé → hold → checkout → payment mock → Worker phát hành QR → check-in.</p><p>PostgreSQL quyết định tồn kho. Retry cùng idempotency key không được tạo giao dịch trùng.</p>",
                f"<h1>Regression</h1><p class='big'>{data['regression']['passed']}/{data['regression']['total']} đạt</p><p>Testcontainers trên máy phát triển; chưa thay thế kiểm tra browser trên VPS.</p>",
                f"<h1>Giữ vé trên TestServer</h1><img alt='Phase 9 p95' src='data:image/png;base64,{images[0]}'><p>Hai lần lặp mỗi mức tải. Không suy ra năng lực mạng/TLS hay SLA.</p>",
                f"<h1>Độ trễ HTTP cần cải thiện</h1><img alt='Phase 10 ramp tails' src='data:image/png;base64,{images[1]}'><p>18 run smoke/ramp/spike đạt; soak dừng lỗi SQL. Chiến dịch chưa hoàn tất.</p>",
                "<h1>AI và triển khai</h1><p>AI chỉ đề xuất; người dùng xác nhận trước khi Booking giữ tồn. Chưa đánh giá chất lượng model thật.</p><p>Đã chuẩn bị Compose, TLS, migration, backup và rollback. Chưa deploy VPS theo phạm vi người dùng yêu cầu.</p>",
                "<h1>Các bước trước khi phát hành</h1><p>Hoàn thành soak, đánh giá AI thật, nghiệm thu browser/VPS, chốt commit + image digest và quay demo thực tế.</p><p>Nguồn: evidence-summary.json, measured-results.md và raw evidence trong gói bàn giao.</p>"]
    html = """<!doctype html><html lang="vi"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>TixFlow · Offline evidence</title><style>
    *{box-sizing:border-box}body{margin:0;background:#0f172a;color:#e2e8f0;font:22px/1.5 system-ui,sans-serif}main{max-width:1200px;margin:auto;padding:36px 60px 100px}section{display:none}section.active{display:block}h1{font-size:44px;color:#5eead4;line-height:1.15}h2{font-size:30px}.big{font-size:72px}img{max-width:100%;max-height:52vh;object-fit:contain;background:white}nav{position:fixed;bottom:0;background:#1e293b;padding:14px;width:100%;display:flex;justify-content:center;gap:20px}button{font:inherit;padding:8px 18px;border:0;border-radius:4px;cursor:pointer}p{max-width:1000px}@media print{nav{display:none}section{display:block;break-after:page}body{color:#111;background:white}}</style><main>"""
    html += "".join(f"<section>{s}</section>" for s in sections)
    html += """</main><nav><button id="prev">← Trước</button><span id="page" aria-live="polite"></span><button id="next">Tiếp →</button></nav><script>
    const slides=[...document.querySelectorAll('section')];let index=0;function show(n){index=Math.max(0,Math.min(slides.length-1,n));slides.forEach((s,i)=>s.classList.toggle('active',i===index));document.getElementById('page').textContent=`${index+1} / ${slides.length}`;}document.getElementById('prev').onclick=()=>show(index-1);document.getElementById('next').onclick=()=>show(index+1);addEventListener('keydown',e=>{if(e.key==='ArrowRight')show(index+1);if(e.key==='ArrowLeft')show(index-1)});show(0);</script></html>"""
    (OUT / "offline-demo.html").write_text(html, encoding="utf-8")


def allowed(path):
    parts = {p.lower() for p in path.parts}
    name = path.name.lower()
    return not (parts & {".git", "node_modules", "bin", "obj", ".next", "backups", "releases", "rendered", "ticket-keys", "assistant-keys", "__pycache__", ".codex-finalizer"}
                or any(p.startswith((".chart-data-", ".presentation-")) for p in parts)
                or (name.startswith(".env") and not name.endswith(".example"))
                or name in {"validation.env", "tokens.json", "state.json", "id_rsa", "id_ed25519"}
                or path.suffix.lower() in {".pem", ".key", ".pfx", ".p12", ".dump", ".log", ".pyc"})


def package(data):
    target = ROOT / "artifacts/phase13/packages" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ") + "-review.zip")
    target.parent.mkdir(parents=True, exist_ok=True)
    files = {}
    for repo in (ROOT, ROOT.parent / "tixflow-frontend"):
        raw = subprocess.check_output(["git", "-C", str(repo), "ls-files", "-z", "--cached", "--others", "--exclude-standard"]).decode("utf-8")
        for item in raw.split("\0"):
            p = repo / item
            if item and p.is_file() and not p.is_symlink() and allowed(Path(item)):
                files[f"{repo.name}/{item}"] = p
    for item in data["inputs"]:
        p = ROOT / item["path"]
        if not allowed(p.relative_to(ROOT)):
            raise ValueError(f"Evidence path is disallowed: {item['path']}")
        files[f"{ROOT.name}/{item['path']}"] = p
    manifest = {"status": "review-candidate", "release_ready": False, "repositories": data["repositories"],
                "created_utc": datetime.now(timezone.utc).isoformat(), "open_gates": data["open_gates"],
                "files": [{"path": name, "bytes": p.stat().st_size, "sha256": digest(p)} for name, p in sorted(files.items())]}
    with zipfile.ZipFile(target, "x", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, p in sorted(files.items()):
            archive.write(p, name)
        archive.writestr("REVIEW-MANIFEST.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    verify(target)
    receipt = {"package": str(target), "sha256": digest(target), "files": len(files), "bytes": target.stat().st_size,
               "release_ready": False, "verified": True}
    write_json(target.with_suffix(".json"), receipt)
    print(json.dumps(receipt, indent=2))


def verify(target):
    with zipfile.ZipFile(target) as archive:
        names = archive.namelist()
        if len(names) != len({n.casefold() for n in names}):
            raise ValueError("Duplicate archive entries (including Windows case collisions)")
        for name in names:
            p = PurePosixPath(name)
            if p.is_absolute() or ".." in p.parts or "\\" in name or ":" in name:
                raise ValueError("Unsafe archive path")
            if not allowed(p):
                raise ValueError(f"Disallowed archive path: {name}")
        manifest = json.loads(archive.read("REVIEW-MANIFEST.json"))
        listed = [r["path"] for r in manifest["files"]]
        if len(listed) != len(set(listed)):
            raise ValueError("Duplicate manifest records")
        expected = {r["path"] for r in manifest["files"]} | {"REVIEW-MANIFEST.json"}
        if set(names) != expected:
            raise ValueError("Manifest entries do not match archive")
        for record in manifest["files"]:
            body = archive.read(record["path"])
            if len(body) != record["bytes"] or hashlib.sha256(body).hexdigest() != record["sha256"]:
                raise ValueError(f"Hash mismatch: {record['path']}")
    print(f"Verified {len(names) - 1} files: {target.name}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", action="store_true")
    parser.add_argument("--verify", type=Path, help="Verify an existing archive without rebuilding or extracting")
    args = parser.parse_args()
    if args.verify:
        verify(args.verify)
    else:
        result = build()
        if args.package:
            package(result)
