# Giai đoạn 10 — Thực nghiệm HTTP bằng k6

Ngày bắt đầu: 19/10/2026, Asia/Saigon. Phase 9 đã có tag `optimized-v1-phase9` tại `2778c393484419c5ef27da0d52306f44f46b38c8`; regression kiểm tra lại đạt 88/88. Phase 10 bổ sung phép đo HTTP thật, dataset riêng và cổng kiểm tra database. Chưa dùng các lần smoke ngắn làm bằng chứng tải tối đa hoặc hoàn tất toàn bộ phase 10.

[Báo cáo smoke đầu tiên](reports/load-v1/README.md): baseline/optimized mỗi bản ba lần, 6/6 đạt tính đúng. Kết quả có cả cải thiện Hold và p95 Checkout tăng trong workload hỗn hợp; cần đo tiếp trước khi kết luận hiệu năng.

## Cách chạy

Yêu cầu Docker Linux containers và PowerShell 7. Script không đọc credential phát triển, không công bố port ra host, không dùng database/volume của stack đang phát triển. Mỗi lần chạy dựng PostgreSQL/RabbitMQ mới trên tmpfs, rồi gỡ đúng project thử nghiệm. Không reset database nghiệp vụ hoặc xóa volume hiện có.

Hai image API/Worker phải chứa mã nguồn và bản build Release trong `/src`, như image runner phase 8/9. Runner kiểm tra toàn bộ hash theo manifest gốc rồi dùng image ID cố định. Mặc định dùng image baseline đã giữ `tixflow-baseline-v1-preserved:phase8` và image optimized `tixflow-baseline-runner:latest`. Nếu thiếu image, tạo lại từ tag tương ứng; không đổi tên image hiện tại thành baseline.

```powershell
# Từ thư mục backend; không chạy song song với benchmark khác.
./scripts/Invoke-Phase10Load.ps1 -Variant both -Repetitions 3

# Từng profile có thư mục kết quả riêng. Vus là VU mỗi scenario của smoke.
./scripts/Invoke-Phase10Load.ps1 -Variant both -Profile ramp -Vus 20 -Repetitions 3
./scripts/Invoke-Phase10Load.ps1 -Variant both -Profile spike -Vus 100 -Repetitions 3
./scripts/Invoke-Phase10Load.ps1 -Variant both -Profile soak -Vus 10 -Repetitions 3

python scripts/Summarize-Phase10.py artifacts/phase10/<timestamp>-smoke
# Tạo PNG/SVG nếu môi trường Python có matplotlib 3.10.7:
python scripts/Plot-Phase10.py artifacts/phase10/<timestamp>-smoke
```

Khôi phục image từ checkout sạch: dùng `git archive --output artifacts/<tag>.tar <tag>`, giải nén vào thư mục mới trong `artifacts`, rồi `docker build -f <checkout>/infrastructure/baseline/Dockerfile -t <image> <checkout>`. Chọn `baseline-v1-phase8` hoặc `optimized-v1-phase9`, truyền image qua `-BaselineImage`/`-OptimizedImage`. Hash gate của runner phải pass trước khi đo. Không chạy script phase 8 trên source optimized để tạo baseline.

## Kịch bản và tiêu chí

`load/scenarios.js` dùng JWT RS256 từ issuer cách ly. Production JwtBearer vẫn kiểm tra issuer, audience, signature, lifetime, identity và role qua discovery/JWKS thật. Issuer không có token endpoint, không xuất private key, không mở port host. Token test tồn tại bốn giờ và chỉ lưu trong thư mục ignored `private`; không đưa thư mục này vào báo cáo hoặc commit. Phép đo không bao gồm đăng nhập Keycloak, PKCE, TLS hay refresh token. Tạo token trước, không gọi login trong iteration.

| Profile | Tải và thời lượng | Nội dung |
|---|---|---|
| smoke | 10 VU × 5 scenario đồng thời, tối đa 50 VU; 100 iteration mỗi scenario, riêng checkout 20 | Browse/search/detail, availability, 100 request tranh 50 vé, 100 request tranh 1 ghế, checkout success/fail và retry/callback lặp |
| ramp | 30s tăng, 2m giữ, 30s giảm | Browse/detail, availability, hold và cancellation mỗi iteration |
| spike | 10s ở 1 VU, tăng đến Vus trong 1s, giữ 30s, giảm 10s | Cùng workload mixed; không mặc định chạy 10.000 VU trên laptop |
| soak | 30s tăng, 30 phút giữ, 30s giảm | Cùng workload mixed, theo dõi tài nguyên/queue qua thời gian |

Trước đo có warm-up catalog và 10 hold/cancel trên loại vé riêng. Custom metrics không đếm setup. Checkout/payment vẫn có thể có chi phí lần gọi đầu; các phiên bản dùng cùng quy trình. Smoke kết hợp read/write trên cùng session, vì vậy các endpoint ảnh hưởng nhau qua DB và CPU.

Mỗi smoke bắt buộc đúng 50 hold quantity, 1 hold hot seat, 10 payment thành công và 10 payment thất bại. Retry hold/order/payment phải giữ nguyên ID; callback lặp phải được xử lý hoặc nhận diện duplicate. Chỉ 409 có mã `INSUFFICIENT_CAPACITY`/`SEAT_UNAVAILABLE` ở hold được coi là conflict dự kiến. 401/403/422, 409 sai mã, timeout/network/5xx là lỗi kỹ thuật. Tách `technical_errors`, `business_conflicts`, `admission_rejections`; mọi check phải pass, lỗi kỹ thuật dưới 1%, 429 bằng 0 trong phép so thuật toán đã tắt limiter. Không dùng `http_req_failed` một mình để kết luận.

Sau mỗi run, đọc invariant trong một SQL snapshot: capacity, held/sold đối chiếu hold/order thực tế, allocation/seat inventory, duplicate hold/order/payment/provider reference/ticket, vé thiếu sau Paid, hold quá hạn trên 60s. Cho Worker tối đa 60s để phát hành vé. Bất kỳ violation hoặc threshold thất bại đều làm script trả lỗi. Lần thất bại giữ artifact để điều tra và không được tổng hợp như kết quả pass.

## So sánh công bằng và dữ liệu thu

Mỗi bản: 1 API, 2 CPU/1 GiB, pool40; PostgreSQL 1 CPU/512 MiB; Worker 0,5 CPU/512 MiB; RabbitMQ 1 CPU/512 MiB; Nginx 0,25 CPU/64 MiB. k6 2 CPU/1 GiB. Đây là cấu hình HTTP phase 10 mới, không phải cấu hình TestServer phase 8. Baseline cache off, optimized cache on; cả hai tắt limiter để đo công việc được thực thi. Cùng seed `load-v1`, image hạ tầng khóa digest, các lần lặp đảo thứ tự phiên bản. DB sử dụng schema đi kèm đúng image nguồn.

Artifact mỗi run gồm:

- `manifest.json`, `images.json`, `compose.yml`: run ID, source revision, image ID, hash harness/dataset/config, host CPU/RAM và cấu hình.
- Runner mới lưu các input vào `inputs/` và đánh trượt run nếu input thay đổi khi đang đo.
- `raw.json.gz`, `summary.json`, `k6.log`: điểm đo k6 và thresholds; giữ cả lần thất bại.
- `database.jsonl`, `resources.jsonl`: connection/lock/outbox và CPU/RAM container có timestamp. Sampler chờ 2s sau mỗi lượt Docker/SQL, nên chu kỳ thực tế lớn hơn 2s; có thể bỏ lỡ lock wait ngắn.
- `queries.json`, `queues.json`, `worker-metrics.json` (optimized): SQL tốn thời gian, queue ready/unacked cuối run, snapshot consumer lag của Worker. Queue cuối run không thay thế time series queue trong spike/soak.
- `invariants.json`, `result.json`: kết quả đối chiếu trực tiếp DB và cổng pass/fail.

`Summarize-Phase10.py` đọc custom HTTP points, bỏ setup, xuất CSV, JSON và Markdown. Báo p50/p95/p99, median/min/max/độ lệch giữa các run. Throughput endpoint chia cho toàn cửa sổ workload đo chung, không phải endpoint chạy cô lập. Các file `private` chứa token/key test không được copy vào báo cáo.

## Điều kiện còn lại để đóng phase 10

Chỉ đánh dấu hoàn tất sau khi có ba lần cho từng cấu hình ramp/steady, spike và soak được chọn, số liệu/charts được rà soát, dashboard Grafana/snapshot cùng cửa sổ đo và ngưỡng SLO được thống nhất. Phép so hiện tại tắt limiter; cần một chiến dịch riêng bật admission control và nhiều replica trước khi quyết định Virtual Waiting Room. Kết quả 429 không phải throughput booking thành công.

Máy hiện tại chạy cả k6 và hệ thống trong Docker Desktop; CPU/RAM giới hạn theo container nhưng vẫn chia sẻ host/kernel/storage, DB tmpfs không đại diện độ bền/IOPS của VPS. Mốc 10.000 VU cần máy tạo tải đủ tài nguyên và nên tách khỏi VPS. Không suy ra 10.000 người dùng đồng thời, production SLA, khả năng chịu tải tối đa hoặc ý nghĩa thống kê từ smoke test ngắn. Tài liệu hiện ghi rõ các phần này còn mở, không tự hạ tiêu chí nghiệm thu của kế hoạch Word.

Tham khảo API k6: [scenarios](https://grafana.com/docs/k6/latest/using-k6/scenarios/), [custom summary](https://grafana.com/docs/k6/latest/results-output/end-of-test/custom-summary/), [Docker execution](https://grafana.com/docs/k6/latest/get-started/running-k6/).
