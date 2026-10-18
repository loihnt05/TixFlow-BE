# Giai đoạn 8 — Baseline v1 và kiểm thử tính đúng

## Phạm vi

Đóng băng backend làm mốc so sánh, không sửa FE. Thuật toán giữ vé Event → Session → TicketType → Inventory, transaction/savepoint và idempotency được giữ nguyên. Sửa duy nhất ở runtime: giới hạn timeout RabbitMQ TCP/handshake/socket read/write ở 5s, heartbeat đề nghị 10s. Phép thử TCP nhận kết nối nhưng không trả AMQP phát hiện timeout TCP hoặc handshake riêng không đủ giới hạn lần đọc socket đầu tiên của client 6.x.

## Tái tạo từ repo sạch

Yêu cầu: Git, Docker chạy Linux containers, Docker Compose, PowerShell 7, kết nối registry/NuGet cho lần build đầu. Không cần `.env`, Keycloak, Redis hoặc database nghiệp vụ. Từ tag `baseline-v1-phase8`:

```powershell
./scripts/Invoke-Phase8Baseline.ps1 -Mode All
```

Script build SDK image cố định, restore `--locked-mode`, build Release và chạy test trong container. Testcontainers tạo PostgreSQL/RabbitMQ riêng, cổng ngẫu nhiên, không tái sử dụng dữ liệu cũ; Resource Reaper và fixture disposal dọn tài nguyên. Baseline có collection/database riêng nên không phụ thuộc thứ tự chạy các integration test khác. `Correctness` bỏ phép đo tải; `Concurrency` chỉ chạy hai lượt 100/500/1.000.

Để debug bằng SDK .NET 8 trên host: `dotnet test TixFlow.sln -c Release --filter 'Category!=Baseline'`. Kết quả tốc độ trên host không được trộn với số đo Docker.

Không có PowerShell có thể chạy `docker compose -f docker-compose.baseline.yml build` rồi `docker compose -f docker-compose.baseline.yml run --rm runner`; TRX/JSON vào `artifacts/phase8`, nhưng muốn manifest SHA-256 đầy đủ phải dùng script PowerShell.

## Cấu hình đóng băng

| Thành phần | Baseline v1 |
|---|---|
| API | 1 ASP.NET host, Release, HTTP pipeline trong TestServer |
| Runner + API + load generator | 2 CPU, 2 GiB, DOTNET_PROCESSOR_COUNT=2 |
| .NET | SDK 8.0.424 bookworm-slim, digest trong Dockerfile |
| PostgreSQL | 16-alpine, digest cố định; 1 CPU/512 MiB, shared_buffers=128MB, max_connections=120 |
| Npgsql | Pool tối đa 40, connection/command timeout 120s; EF retry 5 lần, max delay 10s |
| Booking | Hold 10 phút; lock_timeout 5s; idempotency 24h |
| Observer | Pool riêng 1 connection; pg_stat_activity mỗi 25ms; pg_stat_statements và track_io_timing bật |
| RabbitMQ | 4-management-alpine, digest cố định; 1 CPU/512 MiB; client 6.8.1, TCP/handshake/read/write/continuation 5s, heartbeat 10s |
| Redis/cache | Tắt; không có Redis container trong phép đo |
| Worker | Không chạy hosted worker trong burst giữ vé; mock payment simulator mặc định của API vẫn được đăng ký nhưng không có job |
| Dataset đo | 1 event, 1 session, 1 GA inventory 50 vé, giá 250.000 VND, max/order=2, 1.000 customer có ID cố định |
| Request | 1 vé/request, JWT test 30 phút/customer riêng, key riêng, 100/500/1.000 task xuất phát cùng gate, không retry ở load generator |
| Warm-up/lặp | 10 request trước đo, reset trước mỗi burst; 2 lượt × 3 mức |

Digest PostgreSQL/RabbitMQ nằm trong `TestInfrastructure.cs`; NuGet transitive versions/checksum nằm trong `packages.lock.json`. `manifest.json` ghi hash từng input, source revision/dirty state, Docker host và resource ceilings. Thay các điều kiện này cần version/changelog mới; không chỉnh thuật toán giữa hai lượt rồi gộp báo cáo.

**Giới hạn phép đo:** TestServer chạy pipeline thật, JWT validation và PostgreSQL thật nhưng không đo Kestrel/TLS, network HTTP, OIDC discovery hay FE. API và load generator dùng chung quota. Đây là baseline tranh chấp và tính đúng của backend, không phải cam kết throughput end-to-end hoặc production. Các workload khác trên Docker host và lịch điều phối CPU vẫn gây dao động latency; “cùng kết quả” nghĩa là invariant/số lượng kết quả, không phải số millisecond giống nhau.

## Seed/reset có kiểm soát

`database/baseline/seed.sql` có ID và dữ liệu cố định. `reset.sql` dọn hold/order/payment/outbox của đúng session thực nghiệm rồi phục hồi capacity/counter/version/price. Không TRUNCATE bảng nghiệp vụ. Cả hai từ chối nếu database không phải `tixflow_auth_tests` hoặc thiếu marker `baseline_control` do fixture tạo. Không đưa các script này vào migration nghiệp vụ.

Harness tự seed/reset khi không còn request đang chạy. Khi debug thủ công, dùng `scripts/Invoke-Phase8Dataset.ps1 -Action seed|reset -ContainerId <id>`; script còn kiểm tra label Resource Reaper và image PostgreSQL. Chỉ dùng lúc thực nghiệm đã dừng. Sale window cố định 2020–2099 tránh việc test đặt vé tự hỏng khi ngày thực thay đổi; truy vấn catalog theo tháng 11/2026 vẫn có fixture riêng.

## Automated coverage

- Unit: deadline đúng tại tick trước/đúng/sau expiry; cancel idempotent; paid terminal; session/event transitions; tiền âm, quantity/max/order, capacity, sale window và URL validation; price/fee snapshots giữ nguyên sau catalog edit.
- Integration kế thừa: auth/policy, catalog, reserved seats, all-or-nothing nhiều item, checkout/payment, expiry/payment race, QR/check-in, outbox lease/retry, delayed queue, poison/DLQ, restart Worker.
- Fault mới: hủy HTTP đang chờ row lock → rollback → retry; lỗi nhận response sau commit → replay 1 hold; TCP blackhole lúc AMQP handshake → timeout → outbox retry; 20 callback trùng đồng thời và callback đổi payload; consumer process bị kill đúng sau DB commit trước ACK → broker redelivery → không nhân đôi vé; giá client bị bỏ qua, checkout giữ giá snapshot; quantity sai không tác động inventory.
- Crash probe là executable chỉ có trong test, gọi `TicketDelivery` thật và bị `Process.Kill` tại ranh giới commit/ACK. Nó kiểm chứng crash boundary; kiểm thử hosted Worker thực và restart graceful vẫn nằm trong phase 7. Lost-response test tiêm lỗi ở HTTP handler sau khi server hoàn tất, không giả nhận đó là fault mạng HTTP vật lý.
- Burst: 50 request thành công, phần còn lại `INSUFFICIENT_CAPACITY`; held=50, sold=0, tổng hold item active=50, idempotency hoàn tất=N, oversold=0; bất kỳ 5xx/timeout/REQUEST_IN_PROGRESS đều làm test fail.

## Báo cáo và bằng chứng

Kết quả được ghi vào thư mục timestamp riêng, không ghi đè lần trước:

- `tests.trx`: kết quả từng test và stack trace khi fail.
- `concurrency.json`: latency từng request, p50/p95/p99, throughput, status/error code, invariants, lock samples, top SQL theo tổng thời gian và `EXPLAIN (ANALYZE, BUFFERS)` inventory lookup.
- `manifest.json`: nguồn và điều kiện chạy; không thu `.env`, token, thông tin kết nối cá nhân.

Nghiệm thu ngày 18/10/2026: **79/79 test pass**, không skip. Sáu burst (100/500/1.000 × 2) đều oversold=0 và cùng số lượng kết quả sau reset. [Báo cáo baseline v1](reports/baseline-v1/README.md) lưu bảng số đo, phân tích bottleneck và toàn bộ TRX/JSON/manifest. Tag cục bộ `baseline-v1-phase8` bao gồm mã nguồn và bằng chứng; chưa push remote. Các lần thử lỗi hoặc bị ngắt trước đó không được dùng làm baseline chính thức.

## Nguồn kỹ thuật

Testcontainers lifecycle/module API: [PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/), [RabbitMQ](https://dotnet.testcontainers.org/modules/rabbitmq/), [resource limits](https://dotnet.testcontainers.org/api/low_level_api_access/). Các image/module được pin trong mã nguồn; không tự cập nhật khi chạy lại.
