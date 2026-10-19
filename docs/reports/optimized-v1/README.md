# Optimized v1 — nghiệm thu kỹ thuật giai đoạn 9

Ngày: **18/10/2026**, Asia/Saigon. FE giữ nguyên commit `0c477bcde5e065ec66463c501830efebc95bc98a`.

Nguồn đo: `2778c393484419c5ef27da0d52306f44f46b38c8`, checkout sạch (`dirty=false`), 120 input trong manifest. Build Release 0 warning/error. Tag baseline giai đoạn 8 không thay đổi. Bộ báo cáo không thay đổi mã nguồn được đo.

**90/90 test pass**, 0 failed/0 skipped trong [TRX](tests.trx). Gồm 78 ca correctness kế thừa, 10 ca mới giai đoạn 9, 1 test sáu burst và 1 test đo catalog/QR/query count. [Manifest](manifest.json).

Kiểm tra bổ sung ngày **19/10/2026**: 120/120 hash nguồn vẫn khớp manifest trên; build Release 0 warning/error và **88/88 regression pass**, 0 skipped. [TRX mới](recheck-20261019-tests.trx), [manifest mới](recheck-20261019-manifest.json). Lần này bỏ hai test Category=Baseline, không thay thế phép đo trước/sau ngày 18/10. `dirty=true` do bộ báo cáo chưa được Git theo dõi lúc chạy; các input nguồn đã đối chiếu riêng. Đã bổ sung artifact proxy còn thiếu từ lần smoke gốc và tạo tag local `optimized-v1-phase9` tại commit nguồn trên. Provider AI vẫn chưa được chốt.

## Booking trước/sau

Baseline đối chứng được chạy lại ngay trước optimized từ image cũ, kiểm tra khớp toàn bộ 107 hash với manifest giai đoạn 8. Lần đối chứng chỉ chạy test sáu burst; bộ 79 test baseline đầy đủ vẫn là bằng chứng giai đoạn 8. [Đối chứng mới](reference-concurrency.json), [metadata đối chứng](reference-reference-run.json), [kết quả optimized](concurrency.json).

Cùng TestServer, 1 API, pool40, runner 2 CPU/2 GiB (API + load generator), PostgreSQL 1 CPU/512 MiB, cache/rate limit tắt, 50 vé, 1 vé/request và 10 warm-up. Stack hai replica được dừng trong lúc đo. Không phải phép đo HTTP qua Nginx/TLS/Keycloak và không phải SLA production.

| Lượt | Request | Baseline req/s | Optimized req/s | Baseline p95 (s) | Optimized p95 (s) | Oversold |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 100 | 15.58 | 20.78 | 6.29 | 4.79 | 0 |
| 1 | 500 | 33.99 | 58.29 | 14.16 | 8.22 | 0 |
| 1 | 1000 | 30.77 | 73.11 | 31.00 | 13.21 | 0 |
| 2 | 100 | 15.92 | 53.44 | 6.19 | 1.86 | 0 |
| 2 | 500 | 32.15 | 75.03 | 14.72 | 6.54 | 0 |
| 2 | 1000 | 39.89 | 79.66 | 23.89 | 12.09 | 0 |

Cả hai phiên bản: mỗi burst có đúng 50 Created, còn lại INSUFFICIENT_CAPACITY; held=50, sold=0, active units=50, idempotency hoàn tất=N. Tổng mỗi phiên bản: 3.200 request, 300 Created, 2.900 capacity conflicts, không lỗi kỹ thuật.

- N=100: trung vị throughput 15.75 → 37.11 req/s (2.36×); trung vị p95 6.24 → 3.33s (46.7% giảm).
- N=500: trung vị throughput 33.07 → 66.66 req/s (2.02×); trung vị p95 14.44 → 7.38s (48.9% giảm).
- N=1000: trung vị throughput 35.33 → 76.38 req/s (2.16×); trung vị p95 27.44 → 12.65s (53.9% giảm).

## Bằng chứng DB

- Baseline đối chứng: 95334/95334 tổng số waiter được lấy mẫu ở khóa Event (100.0%); peak Lock waiters=39.
- Optimized: 0/11480 tổng số waiter được lấy mẫu ở khóa Event (0.0%); peak Lock waiters=39.

Các mẫu là quan sát mỗi 25ms, không phải mọi transaction. Tổng thời gian SQL có chồng lấp giữa request, không được cộng thành CPU hoặc wall time. JSON chứa top SQL pg_stat_statements và EXPLAIN (ANALYZE, BUFFERS) của cùng lookup inventory FOR UPDATE để đối chiếu; lookup đó là diagnostic, optimized reserve thực tế dùng UPDATE có điều kiện. Không thêm index khi chưa có bằng chứng lookup là bottleneck.

## Cache, query count và QR

[JSON đầy đủ](catalog-performance.json). Cache off/on ở đây là hai host optimized, để tách tác dụng riêng của cache; không gắn nhãn đây là catalog baseline v1.

| Endpoint | Cache | Request | HIT | Query/request | p50 (ms) | p95 (ms) | Response bytes |
|---|---|---:|---:|---|---:|---:|---:|
| list | off | 50 | 0 | 5–5 | 36.21 | 107.02 | 18504 |
| detail | off | 50 | 0 | 5–5 | 27.89 | 67.19 | 1204 |
| list | on | 50 | 50 | 2–2 | 9.55 | 14.25 | 18504 |
| detail | on | 50 | 50 | 2–2 | 8.76 | 11.14 | 1204 |

Availability luôn đọc live cả trên HIT; test chứng minh held tăng làm available giảm ngay mà vẫn HIT. UPDATE tên/hủy event làm cả hai host bỏ cache cũ. Query counter chỉ đếm HTTP, không đếm poll nền của mock payment simulator.

- Danh sách Hold limit 1/10 dùng [5, 5] query tương ứng (gồm local-user lookup). Mã cũ thực hiện 2 query/Hold ngoài query chung; bằng chứng trước là cấu trúc code, số đo mới xác nhận không tăng query theo N.
- Dựng PNG tại request như trước: 100 lần mất 332.94 ms. Chỉ microbenchmark CPU/I/O key local; không đo auth/DB/network endpoint QR.
- Giải mã PNG đã dựng ở Worker: 100 lần mất 3.96 ms. Chỉ microbenchmark CPU/I/O key local; không đo auth/DB/network endpoint QR.

## Hai replica và giới hạn

Smoke test HTTP thật qua Nginx được lưu tại [proxy-smoke.json](proxy-smoke.json). Hai Kestrel container đều trả catalog, warm cache HIT, health 200; burst bị giới hạn trả 429 và Retry-After. Test tự động riêng chứng minh idempotency replay từ replica khác và 100 request qua hai TestServer vẫn không oversell. Không dùng smoke test thay cho benchmark booking qua Nginx.

Rate limit user/client hiện theo từng replica; IP edge dùng chung. Worker metrics là snapshot 5s với cờ stale, queue count chỉ là ready messages. Virtual Waiting Room chưa triển khai vì chưa có spike/SLO end-to-end; giai đoạn 10 sẽ đo tiếp. Hai lần lặp chỉ chứng minh kết quả nghiệp vụ ổn định và cung cấp số đo sơ bộ; chưa đủ để khẳng định ý nghĩa thống kê hay tải tối đa.

AI: mới có [đề xuất provider/model](../../ai-provider-proposal.md); người dùng chưa chốt, chưa cấu hình key hoặc gọi API trả phí. Đây là đầu việc quyết định còn mở, không phải tính năng AI đã hoàn thành.
