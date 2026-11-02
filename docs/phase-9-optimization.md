# Giai đoạn 9 — Tối ưu có kiểm chứng

Giữ nguyên FE. Mốc giai đoạn 8 là tag `baseline-v1-phase8`; không di chuyển tag hoặc sửa báo cáo baseline. Phiên bản mới: `optimized-v1-phase9`.

## Thay đổi

1. Hold: khóa metadata chia sẻ, batch TicketType/Seat, reserve inventory bằng UPDATE có điều kiện theo ID ổn định. Không còn khóa độc quyền Event cho các Hold cùng đọc metadata. Savepoint, giá snapshot và idempotency vẫn giữ nguyên.
2. Bỏ N+1 khi liệt kê Hold; graph + Order đọc theo tập ID. Giữ hợp đồng response với FE.
3. Cache catalog metadata 30s, 256 entry/replica, generation PostgreSQL vô hiệu hóa sau commit ở mọi replica. Availability/ghế đọc live cho mỗi response. Cache hit/miss quan sát qua header và API Admin.
4. Rate limit IP/user/client theo nhóm endpoint; 429 + Retry-After. Nginx giới hạn IP chung, hai API round-robin và không tự retry mutation. Pool, timeout, Kestrel và CPU/RAM được ghi trong Compose override.
5. Worker dựng PNG QR và mã hóa trước khi lưu; API giải mã sau authorization/state/hash validation. Backfill vé cũ chạy riêng. Mock email/notification tiếp tục idempotent. Worker tổng hợp queue/lag metrics mỗi 5s.

[Quyết định, phạm vi và giới hạn](adr/008-phase9-optimization.md), [bằng chứng và so sánh](reports/optimized-v1/README.md).

## Chạy kiểm chứng

```powershell
./scripts/Invoke-Phase9Verification.ps1 -Mode All
```

Docker runner dùng cùng image SDK, CPU/RAM, PostgreSQL/RabbitMQ, pool40, dataset và sáu burst 100/500/1.000 × 2 như giai đoạn 8. Cache/rate limiter tắt cho burst booking để so sánh thuật toán không bị 429 làm nhiễu. Test bổ sung bật cache/limiter riêng, so cache off/on trên cùng catalog và kiểm tra idempotency/inventory qua hai TestServer. Test catalog đo số query bằng interceptor chỉ đếm HTTP, bỏ poll của simulator. QR microbenchmark chỉ đo dựng/đọc ảnh, không đại diện latency toàn endpoint.

`Correctness` bỏ các phép đo Category=Baseline; `Concurrency` chạy burst và phép đo catalog/QR. Script tạo thư mục timestamp mới, manifest hash nguồn, TRX và JSON; không ghi đè baseline. Muốn lặp baseline gốc, dùng checkout tag giai đoạn 8 và script của tag đó. Nếu đã giữ image baseline cũ, `Invoke-Phase9Reference.ps1` kiểm tra toàn bộ hash với manifest gốc trước khi chạy lại cùng cấu hình.

## Hai API sau Nginx

Từ thư mục backend có `.env` phát triển đã cấu hình, Docker Compose hỗ trợ `!reset`:

```powershell
docker compose -p tixflow-optimized -f docker-compose.yml -f docker-compose.optimized.yml up -d --build --wait nginx worker
./scripts/Test-Phase9Proxy.ps1
docker compose -p tixflow-optimized -f docker-compose.yml -f docker-compose.optimized.yml stop
```

Project `tixflow-optimized` có volume riêng. Stack này dùng các cổng phát triển của stack chính, nên dừng stack chính trước khi chạy; không xóa volume. Chỉ Nginx công bố HTTP API tại localhost:8080, hai API dùng port nội bộ. Subnet proxy 172.30.9.0/24 phải không trùng mạng đã có. Smoke test xác nhận response từ hai instance, cache HIT và 429/Retry-After qua HTTP thật. Kiểm thử booking nhiều instance dùng hai ASP.NET TestServer chia sẻ DB/JWT; không gọi đó là benchmark booking end-to-end qua Nginx.

API Admin: `GET /api/v1/operations/metrics`. Snapshot Worker có thời điểm và cờ stale; counters cache thuộc instance trong response. Các bảng/column mới ở migration 010–011 áp dụng additive và chạy lại an toàn. Triển khai Worker trước/đồng thời API để backfill QR; vé chưa có ảnh tạm trả QR_NOT_AVAILABLE. Backup/giữ signing key volume như giai đoạn 7.

## Các quyết định còn lại

Kiểm tra lại ngày 19/10/2026: 120/120 hash nguồn khớp manifest của lần nghiệm thu 90/90; regression mới đạt 88/88 (không chạy lại hai phép đo Category=Baseline). Đã bổ sung file `proxy-smoke.json` bị thiếu trong bộ báo cáo và tạo tag `optimized-v1-phase9` tại commit `2778c393484419c5ef27da0d52306f44f46b38c8`. Phần kỹ thuật phase 9 đủ để chuyển sang [phase 10 k6](phase-10-load-testing.md); quyết định provider AI vẫn mở trước sprint AI, không coi là đã được người dùng chốt.

- Chưa triển khai Virtual Waiting Room: cần spike test và SLO giai đoạn 10 để quyết định.
- Chưa thay index vì EXPLAIN baseline cho thấy lookup nhỏ; bottleneck có bằng chứng là lock serialization. Query plans và pg_stat_statements nằm trong JSON của cả hai phiên bản.
- Provider/model AI: [đề xuất OpenAI gpt-5.4-mini](ai-provider-proposal.md), chờ người dùng chốt. Chưa gọi dịch vụ trả phí.
- Cấu hình Nginx/Keycloak này phục vụ phát triển/nghiệm thu local; triển khai internet cần cấu hình HTTPS và issuer/host tương ứng. Giới hạn benchmark TestServer không phải throughput production.
