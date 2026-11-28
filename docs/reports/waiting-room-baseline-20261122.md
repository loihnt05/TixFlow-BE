# Baseline trước Virtual Waiting Room — 22/11/2026

Đây là phép đo **hệ thống hiện tại chưa có Waiting Room**. Nó kiểm tra lại luồng bán vé trên mã nguồn hiện tại trước khi triển khai Gate; không chứng minh năng lực production hoặc thời gian khách xếp hàng.

## Nguồn và cách chạy

- Commit nguồn sạch: `e55966bfe881252f9d196be6efeca56db03e4afe` (`dirty=false` trong manifest build), image `sha256:03e8c0aef4c6e2a3bb8d8ccea7f1446e4b3dbc5e9d9045165ad5cbea01536887`.
- `scripts/Build-Phase10Candidate.ps1` build Release và chạy regression: **121 pass, 0 fail, 0 skip**. Build manifest: `artifacts/phase10/build-20261122t032906123z/manifest.json`.
- Harness `http-load-v3`, seed `load-v2`, một API 2 CPU/1 GiB, PostgreSQL 1 CPU/512 MiB, pool 40, Worker/RabbitMQ/Nginx/Prometheus/Grafana và k6 cùng Docker Desktop. Cache bật, rate limiter tắt. Mỗi run có database riêng; issuer cấp JWT thử nghiệm riêng, không đo login/PKCE/TLS.
- Smoke: 10 VU mỗi scenario, tối đa 50 VU, 100 iteration mỗi scenario và 20 checkout. Spike: 1→100 VU trong 1 giây, giữ 30 giây rồi giảm. Cả hai là **closed-loop VU**, không phải arrival-rate cố định.
- Batch gốc: [smoke](../../artifacts/phase10/20261122T033217118Z-smoke/) và [spike](../../artifacts/phase10/20261122T033728147Z-spike/). Mỗi batch có `manifest.json`, `raw.json.gz`, `summary.json`, `invariants.json`, `resources.jsonl`, `database.jsonl`, `queue-samples.jsonl`, `result.json`, `endpoints.csv`, `aggregate.json` và `comparison.md` cho từng run hoặc batch. Thư mục `private` chứa token thử nghiệm không đưa vào báo cáo.

Lệnh đã chạy:

```powershell
.\scripts\Build-Phase10Candidate.ps1
.\scripts\Invoke-Phase10Load.ps1 -Variant optimized -Profile smoke -Vus 10 -Repetitions 3 -OptimizedImage 'tixflow-phase10-candidate:20261122t032906123z' -OptimizedManifest 'artifacts/phase10/build-20261122t032906123z/manifest.json'
.\scripts\Invoke-Phase10Load.ps1 -Variant optimized -Profile spike -Vus 100 -Repetitions 3 -OptimizedImage 'tixflow-phase10-candidate:20261122t032906123z' -OptimizedManifest 'artifacts/phase10/build-20261122t032906123z/manifest.json'
```

## Kết quả

**6/6 run pass.** Mỗi run có k6 exit 0, monitoring không lỗi, **0 invariant violation**. Smoke ở từng run có đúng 50 Hold tranh quota, một Hold thắng ghế duy nhất, 10 payment thành công và 10 thất bại theo k6 threshold. Trên cả hai profile, `oversold`, `duplicate_tickets` và `missing_paid_tickets` đều bằng 0; `technical_errors` và `admission_rejections` bằng 0 trong các endpoint đo. `admission_rejections=0` là đúng vì Gate chưa có và limiter đang tắt.

Median của p95/p99 **tính riêng từng run rồi lấy median ba run**, đơn vị ms:

| Profile | Endpoint | p95 | Khoảng p95 giữa ba run | p99 |
| --- | --- | ---: | ---: | ---: |
| Smoke | Browse | 506,58 | 499,95–1.486,39 | 1.073,25 |
| Smoke | Availability | 685,75 | 610,88–5.944,62 | 1.003,63 |
| Smoke | Hold | **3.102,98** | **2.486,30–10.339,99** | **5.476,12** |
| Smoke | Checkout | 1.549,69 | 1.158,95–2.189,63 | 2.066,99 |
| Smoke | Callback | 2.540,27 | 1.337,68–3.310,05 | 2.941,03 |
| Spike | Browse | 847,28 | 762,78–938,96 | 1.414,87 |
| Spike | Availability | 903,70 | 806,73–1.154,02 | 1.235,05 |
| Spike | Hold | **5.311,18** | **4.986,76–5.951,13** | **6.988,54** |
| Spike | Checkout | 4.186,88 | 3.194,99–7.293,75 | 12.388,34 |
| Spike | Callback | **8.680,14** | **7.981,07–9.605,76** | **13.277,64** |

Smoke có 149 conflict Hold do hết quota/ghế trên 220 lần gọi `hold` mỗi run; đây là kết quả nghiệp vụ dự kiến, không phải 149 lỗi kỹ thuật. Spike dùng ticket type checkout có capacity lớn để không cạn kho trong phép đo mixed. Latency của callback và độ dao động p95 Hold cần theo dõi khi đo lại; không lấy việc mọi run pass để kết luận độ trễ đã tốt.

## Soak lịch sử chưa đóng

Chiến dịch 22/10/2026 vẫn `failed` tại `artifacts/phase10/20261022T140340222Z-soak/baseline-1`. `failure-services.log` ghi PostgreSQL nhận `fast shutdown request` lúc **14:05:32 UTC**; Prometheus cũng nhận SIGTERM cùng thời điểm, RabbitMQ ghi shutdown. Sampler bắt đầu lỗi database từ 14:05:32, sau đó không tìm thấy service PostgreSQL; k6 vẫn chạy tới 14:35 và thất bại vì dịch vụ biến mất. Mẫu tài nguyên ngay trước đó ghi PostgreSQL khoảng 159 MiB/512 MiB và CPU gần một core. Bằng chứng này cho thấy nhiều service bị dừng đồng thời; **chưa xác định ai hoặc cơ chế nào gửi tín hiệu dừng**, và không đủ căn cứ quy lỗi cho OOM hay logic PostgreSQL.

Trước khi công bố độ bền dài hạn: bổ sung/giữ Docker events, exit code, `OOMKilled`, log Docker Desktop và trạng thái host ngay khi service dừng; chạy lại soak trên stack cách ly, giữ nguyên run lỗi. Không trộn số liệu soak lỗi vào batch smoke/spike đạt.

## Giới hạn so sánh sau triển khai

Profile k6 hiện nay gọi thẳng Hold, tắt limiter và không có Redis. Để đo Waiting Room, cần profile riêng với 100 Customer vào hàng theo lịch đến đã ghi, `join/status/grant/hold`, thời gian chờ và quyết định hết vé. Phải chạy Gate off/on trên **cùng image, cùng dataset, tài nguyên và offered load**; bảng trên là mốc hiện trạng, chưa phải phép A/B Waiting Room. Hợp đồng và ngưỡng local dự kiến nằm ở [phase 1](../virtual-waiting-room-phase-1.md).
