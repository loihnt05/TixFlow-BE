# ADR-008: Tối ưu giai đoạn 9 dựa trên baseline v1

Ngày: 18/10/2026. Trạng thái: triển khai, kết quả kiểm chứng tại `docs/reports/optimized-v1`.

## Booking và truy vấn

Baseline v1 có 100% mẫu chờ Lock nằm ở Event `FOR UPDATE`. Lookup inventory chỉ mất khoảng 0,073–0,128 ms trên dataset nhỏ. Vì vậy chưa thêm index dựa trên phỏng đoán; giữ index/constraints hiện có và lưu lại EXPLAIN cùng thống kê SQL sau thay đổi.

Create Hold dùng Event → Session → các TicketType theo ID với `FOR SHARE`. Chỉ đọc metadata ở các bước này; PostgreSQL chặn UPDATE/DELETE metadata cho tới cuối transaction nhưng cho nhiều Hold đọc đồng thời. Reserved seats khóa độc quyền theo ID trước khi reserve inventory; cập nhật inventory theo TicketType ID bằng `UPDATE ... WHERE available >= quantity`. Số row cập nhật là quyết định cấp tồn. Savepoint, idempotency và constraints không thay đổi. Các transition checkout/payment/expiry/management vẫn giữ khóa cha độc quyền. [PostgreSQL 16 row locks](https://www.postgresql.org/docs/16/explicit-locking.html).

TicketType/Seat được đọc và khóa theo batch, bỏ truy vấn khóa inventory dư thừa. Danh sách Hold đọc graph và Order theo tập ID thay vì hai query cho từng Hold. API/DTO giữ tương thích; không bỏ trường FE đang dùng. Catalog không đọc lịch sử allocation vào graph metadata; trạng thái ghế lấy từ allocation đang Held/Sold.

## Cache và nhiều replica

Đây là điều chỉnh có chủ đích so với đề xuất Redis trong ADR-002: dùng cache metadata trong RAM mỗi replica, tối đa 256 entry, TTL 30s. Khóa gồm generation DB + path/query. Trigger statement trên Event/Session/TicketType/Organizer/Venue/Seat tăng generation trong chính transaction sửa catalog. Mỗi lần đọc cache kiểm tra generation đã commit nên mọi replica tự bỏ qua bản cũ, không phụ thuộc Pub/Sub hoặc best-effort eviction. Fill trùng được gộp bằng 32 semaphore cố định; không tạo dictionary lock vô hạn theo input người dùng.

Inventory/allocation không tăng generation, tránh đưa một hàng khóa toàn cục vào đường ghi booking. Mỗi response (kể cả HIT) lấy inventory và ghế đang Held/Sold trực tiếp từ DB. Không cache Hold/Order/ticket riêng tư. Cache chỉ giữ response 200; query dài hơn 2048 ký tự bỏ qua cache. DB lỗi thì không phục vụ metadata cũ bỏ qua kiểm tra generation. Hai request có thể quan sát các commit khác nhau; booking luôn kiểm tra lại trong transaction. GET catalog trả no-store để browser/proxy không giữ availability cũ.

Không có session đăng nhập trong RAM API. JWT xác thực bằng Keycloak; quyền người dùng, idempotency, booking và vé ở PostgreSQL. API/Worker dùng chung volume key. Mất cache hoặc restart replica không mất giao dịch. Redis trong Compose hiện chưa tham gia đường xử lý này.

## Rate limit và tài nguyên

Nginx là cổng HTTP duy nhất, round-robin hai replica, không sticky session và không tự retry POST. Edge limiter dùng IP thật, 20 request/s + burst 40; ghi đè X-Forwarded-For từ client. API chỉ tin IP proxy 172.30.9.10, áp dụng cửa sổ 60 giây theo IP, subject JWT và azp client, chia phạm vi booking/read/write. Endpoint hold bao gồm GET và mutation trong nhóm booking. Rate limit trả 429, code RATE_LIMITED, Retry-After và no-store. [ASP.NET rate limiter](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0), [Nginx request limiter](https://nginx.org/en/docs/http/ngx_http_limit_req_module.html).

Hạn mức User/Client của API là **mỗi replica**, không phải quota phân tán: hai replica cho trần tổng tối đa gấp đôi. Edge IP áp dụng chung tại Nginx. Restart API reset cửa sổ cục bộ. Chưa dùng giới hạn này làm quota thanh toán hoặc cam kết chống lạm dụng theo tài khoản trên toàn cluster; khi cần quota toàn cluster sẽ bổ sung Redis atomic limiter và kiểm thử lỗi Redis riêng.

Mỗi API 1 CPU/768 MiB, pool 20; Worker 0,5 CPU/512 MiB, pool 10; PostgreSQL 1 CPU/512 MiB, max_connections 120; RabbitMQ 1 CPU/512 MiB. Connection timeout 10s, command 30s. Kestrel: 512 connection, body tối đa 1 MiB, header 15s, keepalive 30s. Đây là cấu hình triển khai hai replica, **không trộn** với phép đo so sánh một host/pool40 ở baseline.

## Worker và quan sát

Worker dựng ảnh PNG QR lúc phát hành; lưu ciphertext AES-GCM trong DB, key dẫn xuất HMAC với domain riêng từ signing key, nonce ngẫu nhiên và AAD là Ticket ID. API vẫn kiểm tra ownership/state/hash token trước khi giải mã ảnh. Không lưu PNG/token rõ; không đổi định dạng QR/FE. Worker backfill tối đa 20 ảnh cũ mỗi 5s; vé chưa backfill trả QR_NOT_AVAILABLE. Phải giữ key cũ và backup key volume cùng DB.

Email/InApp vẫn là mock effect có unique Order/channel. Worker tổng hợp metrics mỗi 5s: ready queue/retry/DLQ, pending outbox, tuổi outbox cũ nhất, consumer lag lớn nhất trong 5 phút và số QR chưa dựng. Queue count là ready messages; không phải unacked. API Admin `/api/v1/operations/metrics` trả cache hit/miss theo replica và snapshot Worker kèm stale sau 20s. Khi không có Worker, danh sách rỗng; không coi đó là queue đã sạch.

## Virtual Waiting Room

Chưa triển khai. Giai đoạn 9 đo contention và thêm rate limit trước; chưa có SLO end-to-end hoặc spike test k6 để biện minh cho hàng chờ người dùng. Giai đoạn 10 đo p95/p99/timeout qua Nginx, DB lock wait và 429; chỉ thêm waiting room khi vượt ngưỡng vận hành đã thống nhất. Điều này tránh thay đổi giao diện FE khi chưa có nhu cầu được đo.
