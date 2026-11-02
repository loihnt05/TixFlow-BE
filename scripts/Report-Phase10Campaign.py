"""Create a reviewable local report from a fully measured campaign.

No credentials/private mounts are copied. Raw data remains in artifacts, with
an explicit file inventory and SHA-256 hashes. This never marks a partial
campaign complete, or treats a proposed SLO as independently validated.
"""
import argparse
import csv
import hashlib
import json
import math
import shutil
import statistics
import subprocess
import sys
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main(campaign, output):
    state = read(campaign / "status.json")
    if state["status"] != "measured":
        raise ValueError(f"Campaign is {state['status']}; no completion report will be emitted")
    batches = {item["profile"]: Path(item["path"]) for item in state["batches"]}
    if set(batches) != {"smoke", "ramp", "spike", "soak"}:
        raise ValueError("All four profiles are required")
    source = Path(__file__).resolve().parent
    output.mkdir(parents=True, exist_ok=True)
    inventory = []
    evidence = []
    aggregates = {}
    operations = {}
    campaign_config = None
    campaign_sources = None
    for profile, batch in batches.items():
        subprocess.run([sys.executable, str(source / "Summarize-Phase10.py"), str(batch)], check=True)
        subprocess.run([sys.executable, str(source / "Analyze-Phase10Monitoring.py"), str(batch)], check=True)
        subprocess.run([sys.executable, str(source / "Plot-Phase10.py"), str(batch)], check=True)
        aggregate = read(batch / "aggregate.json")
        expected_vus = {'smoke':10, 'ramp':20, 'spike':100, 'soak':10}[profile]
        if aggregate['configuration']['profile'] != profile or aggregate['configuration']['vus'] != expected_vus:
            raise ValueError(f'Unexpected workload for {profile}')
        config = {key: value for key, value in aggregate['configuration'].items()
                  if key not in ('profile', 'vus', 'iterations')}
        if campaign_config is not None and config != campaign_config:
            raise ValueError(f'Incompatible harness/resources across profiles: {profile}')
        if campaign_sources is not None and aggregate['sources'] != campaign_sources:
            raise ValueError(f'Different source images across profiles: {profile}')
        campaign_config, campaign_sources = config, aggregate['sources']
        aggregates[profile] = aggregate
        operations[profile] = read(batch / 'monitoring-analysis.json')
        for variant in ("baseline", "optimized"):
            if len([r for r in aggregate["runs"] if r["variant"] == variant]) < 3:
                raise ValueError(f"Fewer than three runs: {profile}/{variant}")
        for run in aggregate["runs"]:
            minimum_seconds = {'smoke':0, 'ramp':175, 'spike':46, 'soak':1855}[profile]
            if run['measured_seconds'] < minimum_seconds:
                raise ValueError(f'Truncated workload: {run["run_id"]}')
            folder = next(p for p in batch.iterdir() if p.is_dir() and (p / "manifest.json").exists()
                          and read(p / "manifest.json")["runId"] == run["run_id"])
            manifest = read(folder / "manifest.json")
            for item in manifest["inputs"]:
                if sha(folder / "inputs" / item["path"]) != item["sha256"]:
                    raise ValueError(f"Frozen input hash mismatch: {folder}/{item['path']}")
            monitor = read(folder / "monitoring-result.json")
            if not monitor.get("verified"):
                raise ValueError(f"Unverified monitoring: {folder}")
            subprocess.run([sys.executable, str(source / "Format-Phase10Snapshot.py"),
                str(folder / "grafana-snapshot.json"), str(output / profile / "snapshots" / f"{folder.name}.json")], check=True)
            rows = list(csv.DictReader((batch / "endpoints.csv").open(encoding="utf-8")))
            selected = [row for row in rows if row["run_id"] == run["run_id"]]
            invariant = run["invariants"]
            if any(value != 0 for key, value in invariant.items() if key not in ("inventory", "paid_orders", "tickets")):
                raise ValueError(f"Invariant violation: {folder}")
            evidence.append({"profile":profile, "variant":run["variant"], "runId":run["run_id"],
                "requests":run["requests"], "seconds":run["measured_seconds"],
                "technicalErrors":sum(int(r["technical_errors"]) for r in selected),
                "businessConflicts":sum(int(r["business_conflicts"]) for r in selected),
                "admissionRejections":sum(int(r["admission_rejections"]) for r in selected),
                "oversold":invariant["oversold"], "paidOrdersIncludingWarmup":invariant["paid_orders"],
                "ticketsIncludingWarmup":invariant["tickets"], "path":str(folder),
                "browseCacheHitRate":read(folder / 'summary.json')['metrics']['catalog_cache_hits']['values']['rate']})
            for name in ("raw.json.gz", "summary.json", "manifest.json", "invariants.json", "result.json",
                         "database.jsonl", "resources.jsonl", "queue-samples.jsonl", "queries.json",
                         "prometheus-range.json", "grafana-snapshot.json", "monitoring-result.json"):
                file = folder / name
                inventory.append({"runId":run["run_id"], "path":str(file), "bytes":file.stat().st_size, "sha256":sha(file)})
        destination = output / profile
        destination.mkdir(exist_ok=True)
        for name in ("aggregate.json", "endpoints.csv", "comparison.md", "comparison.png", "comparison.svg", "monitoring-analysis.json"):
            shutil.copyfile(batch / name, destination / name)
    # Propose headroom above the worst per-run p95 in ramp; do not declare an
    # independent SLO pass using the same observations used to choose the SLO.
    ramp = {r["endpoint"]:r for r in aggregates["ramp"]["endpoints"] if r["variant"] == "optimized"}
    slo = {}
    for name, endpoints in {"Read":["browse","detail","availability"], "Hold":["hold"], "Checkout":["checkout"]}.items():
        observed = max(ramp[e]["p95_ms"]["max"] for e in endpoints)
        slo[name] = {"observedWorstRunP95Ms":observed, "proposedP95Ms":math.ceil(observed * 1.2 / 100) * 100,
                     "validation":"proposal only; requires a future independent run"}
    (output / "evidence.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    (output / "raw-files.json").write_text(json.dumps(inventory, indent=2), encoding="utf-8")
    (output / "slo-proposal.json").write_text(json.dumps(slo, indent=2), encoding="utf-8")
    lines = ["# Phase 10 — kết quả chiến dịch local", "", f"Campaign: `{campaign.name}`. {len(evidence)} run đạt cổng đo; baseline/optimized mỗi profile ít nhất 3 lần.", "",
             "## Phạm vi", "", "Một API 2 CPU/1 GiB, PostgreSQL 1 CPU/512 MiB, pool 40; k6 và monitoring cùng Docker Desktop. Dataset load-v2, cache tắt ở baseline/bật ở optimized, limiter tắt. Smoke: 10 VU mỗi scenario; ramp: 20 VU (30s tăng, 2m giữ, 30s giảm); spike: 100 VU; soak: 10 VU (30s tăng, 30m giữ, 30s giảm). Đây là phép so trong workload đã công bố, không phải kiểm chứng production SLA hoặc 10.000 người dùng.", "",
             "Các manifest ghi container khác dùng chung host. Monitoring bao gồm warm-up và 6s cuối; p50/p95/p99 và throughput chính thức loại warm-up, lấy từ raw custom metrics. Đây là closed-loop VU, không phải arrival-rate cố định; khi server chậm, tốc độ gửi có thể giảm. Throughput tính cả cửa sổ đo, không đại diện capacity cực đại. Consumer lag là max trong cửa sổ 5 phút; CPU/RAM/lock/queue là các mẫu rời rạc.", "",
             "## Tính đúng", "", "| Profile | Bản | Run | HTTP requests | Lỗi kỹ thuật | Conflict dự kiến | 429 | Oversold |", "|---|---|---:|---:|---:|---:|---:|---:|"]
    for profile in batches:
        for variant in ("baseline","optimized"):
            selected = [r for r in evidence if r["profile"] == profile and r["variant"] == variant]
            values = [sum(r[k] for r in selected) for k in ("requests","technicalErrors","businessConflicts","admissionRejections","oversold")]
            lines.append(f"| {profile} | {variant} | {len(selected)} | " + " | ".join(map(str, values)) + " |")
    lines += ["", "Mọi invariant trong các run được đưa vào bảng bằng 0, gồm đối chiếu capacity/held/sold, ghế trùng, order/payment/ticket trùng, vé thiếu và hold quá hạn. Số paid/ticket cuối DB có thêm 5 giao dịch warm-up; raw custom counters chỉ đếm workload đo.", "", "## Độ trễ và throughput", "", "Các số sau là median của p95 từng run, không phải p95 gộp. Min/max/stdev và p50/p99 có trong bảng chi tiết từng profile."]
    for profile, aggregate in aggregates.items():
        lines += ["", f"### {profile}", "", "| Profile | Endpoint | Baseline p95 ms | Optimized p95 ms | Thay đổi |", "|---|---|---:|---:|---:|"]
        points = {(r["variant"],r["endpoint"]):r for r in aggregate["endpoints"]}
        for endpoint in ("browse","availability","hold","checkout","payment","callback"):
            before=points[("baseline",endpoint)]["p95_ms"]["median"]
            after=points[("optimized",endpoint)]["p95_ms"]["median"]
            lines.append(f"| {profile} | {endpoint} | {before:.2f} | {after:.2f} | {(after/before-1)*100:+.1f}% |")
        lines += ["", f"[Bảng {profile}]({profile}/comparison.md) · [Operational metrics]({profile}/monitoring-analysis.json)", "", f"![{profile}]({profile}/comparison.png)", ""]
    lines += ["## Tài nguyên và hàng đợi", "",
              "Các cột peak là median của giá trị lớn nhất quan sát trong mỗi run, bao gồm warm-up và đuôi đo. Mẫu rời rạc có thể bỏ sót đỉnh ngắn. CPU 100% tương đương một core; API giới hạn hai core. Queue là peak lớn nhất của một queue, không phải tổng đồng thời. Cache là median hit rate của browse/search.", "",
              "| Profile | Bản | API CPU peak % | API RAM peak MiB | DB connections peak | Lock waiters peak | Outbox lag peak s | Queue ready peak | Cache hit % |",
              "|---|---|---:|---:|---:|---:|---:|---:|---:|"]
    for profile, samples in operations.items():
        for variant in ('baseline', 'optimized'):
            selected = [r for r in samples if r['variant'] == variant]
            values = [statistics.median([fn(r) for r in selected]) for fn in (
                lambda r: r['resources']['api']['cpu_percent']['max'],
                lambda r: r['resources']['api']['memory_bytes']['max'] / 1048576,
                lambda r: r['database']['connections']['max'],
                lambda r: r['database']['lock_waiters']['max'],
                lambda r: r['database']['oldest_outbox_seconds']['max'],
                lambda r: max((q['messages_ready']['max'] for q in r['queues'].values()), default=0))]
            values.append(statistics.median([r['browseCacheHitRate'] * 100 for r in evidence
                                            if r['profile'] == profile and r['variant'] == variant]))
            lines.append(f'| {profile} | {variant} | ' + ' | '.join(f'{v:.2f}' for v in values) + ' |')
    lines += ["", "## SLO local đề xuất", "", "Dùng workload ramp 20 VU cùng cấu hình trên. Lấy p95 cao nhất giữa ba run optimized, thêm 20% khoảng dự phòng rồi làm tròn lên 100 ms. Đây là ngưỡng khởi điểm để kiểm chứng tiếp, không phải SLO đã đạt trên dữ liệu độc lập.", "", "| Nhóm | p95 quan sát cao nhất ms | Ngưỡng đề xuất ms |", "|---|---:|---:|"]
    for name, item in slo.items():
        lines.append(f"| {name} | {item['observedWorstRunP95Ms']:.2f} | {item['proposedP95Ms']} |")
    lines += ["", "Giữ mục tiêu lỗi kỹ thuật <1%, oversold/duplicate =0 và trễ trả tồn sau expiry ≤60s theo kế hoạch. Tăng tải hoặc bật limiter/nhiều replica cần đo lại; 429 phải tách khỏi booking thành công.", "", "## Tái chạy và bằng chứng", "", "[Runbook](../../phase-10-load-testing.md), [tóm tắt từng run](evidence.json), [đường dẫn và SHA-256 raw metrics/snapshots](raw-files.json). Raw được giữ trong `artifacts/phase10`, không copy thư mục private chứa JWT/key. Mỗi thư mục profile có `snapshots/` chứa bản trình bày để import qua API Grafana: nhãn ngắn, tỷ lệ 0–100%, cắt khoảng đầu chưa có mẫu; tool kiểm tra toàn bộ numeric points giữ nguyên. Snapshot nguyên bản vẫn có hash trong raw-files.json. Báo cáo cũ load-v1 và [các lần thử lỗi](calibration.md) vẫn được giữ riêng; không trộn chúng vào phép so load-v2.", "", "Bản Checkout mới giữ khóa chia sẻ trên Event/Session chỉ đọc, giữ khóa độc quyền trên Hold. Regression đạt 90/90; kiểm thử đối chứng trước sửa tái hiện bị chặn bởi shared reader. Phép so baseline phase 8 với candidate gồm toàn bộ thay đổi phase 9 và Checkout, nên không quy toàn bộ chênh lệch hiệu năng cho riêng sửa Checkout."]
    (output / "README.md").write_text("\n".join(lines)+"\n", encoding="utf-8")
    (output / "report-tools.json").write_text(json.dumps([
        {"path":str(path), "sha256":sha(path)} for path in (source / name for name in
        ("Report-Phase10Campaign.py","Summarize-Phase10.py","Analyze-Phase10Monitoring.py","Plot-Phase10.py","Format-Phase10Snapshot.py"))
    ], indent=2), encoding="utf-8")
    print(output / "README.md")


if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("campaign",type=Path)
    parser.add_argument("output",type=Path)
    args=parser.parse_args()
    main(args.campaign.resolve(),args.output.resolve())
