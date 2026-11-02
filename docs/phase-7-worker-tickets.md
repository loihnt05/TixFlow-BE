# Giai đoạn 7 — Expiry, RabbitMQ, vé QR và check-in

## Phạm vi

- Worker quét hold Active quá hạn mỗi 2 giây, batch 100. PostgreSQL quyết định deadline; khóa Event → Session → Hold → Order/Payment → inventory. `SKIP LOCKED` bỏ qua aggregate đang bận, kiểm tra lại trạng thái trong transaction. Inventory, seat, Hold, Order và Payment cùng commit; chạy lại không trả tồn lần hai.
- Payment thành công ghi `OrderPaid` vào outbox trong cùng transaction settlement. Migration 009 bổ sung yêu cầu phát hành cho các Order Paid có trước giai đoạn 7.
- Publisher claim bằng lease 60 giây, gửi persistent message với mandatory routing và publisher confirms rồi mới đánh dấu đã phát. Lỗi broker dùng backoff 2–60 giây; lease hết hạn tự phục hồi sau crash. Expiry chạy độc lập với broker.
- Consumer dùng inbox PostgreSQL và transaction: kiểm tra Order Paid, phát hành một vé cho mỗi đơn vị OrderItem, gắn đúng ghế Sold, lưu inbox và sự kiện `TicketsIssued` trước ACK. Unique `(order_item_id, ordinal)` và unique ghế bảo vệ cả trường hợp message ID khác nhau.
- Tác vụ mock tạo hai bản ghi Email/InApp trong `mock_notifications`, unique theo Order/channel. Không gửi email thật.
- Trang vé tài khoản lấy API và cập nhật mỗi 5 giây khi tab đang hiển thị. Giữ nguyên bố cục, class/style và modal; thay QR trang trí bằng PNG QR thật.

## API

| Method | Endpoint | Quyền và kết quả |
| --- | --- | --- |
| GET | `/api/v1/tickets?orderId={id}` | Customer/Admin đọc vé của chính mình; tối đa 500 vé mới nhất, optional lọc Order |
| GET | `/api/v1/tickets/{id}` | Chủ vé đọc chi tiết; tài khoản khác nhận 404 |
| GET | `/api/v1/tickets/{id}/qr` | Customer owner hoặc Admin có `supportReason`; Admin được audit |
| POST | `/api/v1/check-ins` | Organizer sở hữu Event hoặc Admin; header `Idempotency-Key`; alias `/tickets/check-ins` |

DTO vé gồm `id`, `ticketCode`, `orderId`, `ownerId`, thông tin event/date/venue hiện tại, tên loại vé từ snapshot của OrderItem, `seatLabel`, `status`, `checkedInAtUtc`. Danh sách/chi tiết không trả token. Chỉ GET QR riêng trả `qrToken` và `qrImageDataUrl`. Các route vé dùng `Cache-Control: no-store`.

Token QR là HMAC-SHA256 với domain riêng, Ticket ID và nonce ngẫu nhiên 128 bit; không chứa thông tin cá nhân, giá hoặc ID đơn. Database chỉ lưu hash, nonce, key ID; token gốc không được lưu. API tái tạo proof từ metadata và kiểm tra hash constant-time trước khi trả. QRCoder tạo PNG tại API, không gửi token cho dịch vụ ngoài.

API và Worker dùng chung volume `ticket_keys:/app/ticket-keys`. Key 256 bit được tạo bằng ghi file tạm rồi đổi tên nguyên tử; file key trên Linux có quyền 0600. `Tickets:KeyDirectory` và `Tickets:ActiveKeyId` cấu hình vị trí/key mới. Phải backup volume key cùng database và giữ key cũ cho vé đang hiệu lực; thay active key không xóa file cũ. Khi chạy ngoài Docker, API và Worker phải trỏ `Tickets__KeyDirectory` về cùng thư mục bền vững.

Check-in nhận `eventSessionId` cùng một trong hai trường `qrToken` hoặc `ticketCode`. Manual entry luôn cần `reason`; Admin cần reason cả khi dùng QR. Cửa sổ hợp lệ là `[starts_at_utc - 60 phút, ends_at_utc)` theo giờ PostgreSQL. Sai session/ownership bị từ chối. Chuyển `Issued` → `Used`, actor/key/hash request/thời gian và audit QR/manual cùng transaction. Cùng actor + key + payload trả lại nguyên kết quả; payload khác trả 409 `IDEMPOTENCY_KEY_REUSED`. Key mới quét vé Used trả 409 `TICKET_ALREADY_USED`. Parent hủy/hoàn hoặc Session Completed bị từ chối. Ledger/audit không lưu token gốc.

## RabbitMQ và phục hồi

Topology được Worker khai báo durable, không cần import thủ công:

| Queue | Công dụng |
| --- | --- |
| `tixflow.post-payment.v1` | OrderPaid và TicketsIssued |
| `.retry.1`, `.retry.2`, `.retry.3` (thêm sau tên queue chính) | TTL 1, 5 và 30 giây, dead-letter về queue chính |
| `tixflow.post-payment.v1.dead` | Message sai schema/type và message hết lượt retry |

Envelope giữ `Id`, `Type`, `Payload`, `CorrelationId`; message properties giữ MessageId/CorrelationId. Order ID được dùng làm correlation xuyên suốt hậu giao dịch. Republish retry/DLQ có confirm trước ACK message gốc. Khi DB hoặc broker mất kết nối, việc có thể được gửi lại; nghiệp vụ được chống lặp trong PostgreSQL. Publisher tiếp tục retry khi broker mất kết nối, không bỏ message vì vượt số lần thử.

DLQ cần người vận hành xem metadata và sửa nguyên nhân trước khi đưa lại queue chính. Giữ nguyên MessageId/CorrelationId khi redrive; không xóa inbox đã commit. Không log token QR/payload. Mock payment simulator giai đoạn 6 vẫn chạy trong API và xử lý riêng `MockPaymentRequested`; đây là tác vụ giả lập provider, không đi qua queue phát hành vé.

## Chạy và kiểm chứng

```powershell
dotnet test tests/TixFlow.Api.Tests -c Release --filter 'FullyQualifiedName~Phase7Tests|FullyQualifiedName~Phase8FaultTests' --logger 'trx;LogFileName=phase7-review.trx' --results-directory artifacts/phase7-review
```

Từ giai đoạn 8, cần Docker đang chạy: test tự tạo PostgreSQL/RabbitMQ bằng Testcontainers, dùng cổng host ngẫu nhiên và tự dọn container sau khi chạy; không dùng dữ liệu của stack phát triển. Không cần bật `docker-compose.test.yml`; cấu hình cổng cố định trong file đó chỉ còn dành cho kiểm tra thủ công. Chạy toàn bộ suite bằng `dotnet test TixFlow.sln -c Release`.

`Phase7Tests` có 14 ca: số lượng vé khi message trùng/song song, QR riêng/ownership/audit, key rotation, cửa sổ check-in/session/manual entry, 10 lượt quét tranh cùng vé, replay/key reuse, expiry nhiều Worker, expiry/payment race, ghế cố định, lease bị bỏ dở, broker unavailable, retry backoff, consumer đóng trước ACK, duplicate RabbitMQ và poison message/DLQ. Hosted Worker được chạy xuyên suốt expiry → outbox → ticket → notification rồi restart. Có test checkout/settlement với EF retry strategy giống runtime. Bộ authentication còn kiểm tra first-login cạnh tranh; unique email chỉ retry khi đúng cùng OIDC subject.

Kết quả rà soát lại ngày 18/10/2026:

- Đối chiếu các hạng mục giai đoạn 7 trong kế hoạch triển khai: expiry nhiều Worker, giải phóng inventory/seat, transactional outbox, retry/DLQ/correlation, QR, phát hành sau Paid, check-in, mock notification và trang vé đều có triển khai và kiểm thử liên quan.
- Lệnh kiểm tra chọn lọc ở trên: **23/23 pass**, không thất bại hoặc bỏ qua; gồm 14 ca `Phase7Tests` và 9 ca `Phase8FaultTests`. Báo cáo cục bộ: `artifacts/phase7-review/phase7-review.trx` (không đưa vào Git).
- Bao gồm kiểm thử kill tiến trình consumer sau commit trước ACK, redelivery không nhân đôi vé, và API đọc được QR với key dùng chung; broker handshake timeout không làm mất outbox.
- Frontend `npm run build`: pass, gồm TypeScript và sinh trang. Không chỉnh sửa mã nguồn hoặc giao diện FE trong lần rà soát này; chưa chạy kiểm tra tương tác trình duyệt trong lần này.
- Chưa phát hiện lỗi chức năng trong phạm vi đã rà soát. Chỉ sửa hướng dẫn test đã lỗi thời và mô tả DTO trong tài liệu; không thay thuật toán hoặc tag baseline giai đoạn 8. Không khởi động lại stack phát triển trong lần kiểm tra này.

Kết quả nghiệm thu lịch sử ngày 15/10/2026:

- `dotnet test TixFlow.sln --no-restore`: 54/54 test pass, không bỏ qua test; bao gồm 14 ca giai đoạn 7 trên PostgreSQL/RabbitMQ cách ly.
- `npm run build`: pass, gồm biên dịch production, TypeScript và sinh trang. Các lỗi FE được ghi nhận ở lần kiểm tra trước đã được sửa trong mã nguồn hiện tại.
- RabbitMQ test khởi động với user `100:101`, tmpfs đúng quyền; healthcheck xác nhận ứng dụng và cổng AMQP sẵn sàng, tránh lỗi quyền cookie do healthcheck chạy khác user.
- `docker compose up -d --wait api worker`: thành công; migration áp dụng lại an toàn, API/Worker và dịch vụ phụ thuộc chạy ổn định.
- HTTP `/health` trả 200; Swagger có `/api/v1/tickets`, `/api/v1/tickets/{ticketId}/qr` và `/api/v1/check-ins`.
- Phần tích hợp FE của giai đoạn 7 chỉ nối dữ liệu vé và tải QR khi mở modal. Không thay đổi thiết kế giao diện trong phần việc này.

Áp dụng cho stack phát triển có dữ liệu:

```powershell
docker compose up -d --build api worker
docker compose logs --tail 50 worker
```

Compose chạy migration 009 theo thứ tự trước API; không xóa volume. Migration bổ sung bảng inbox, notification, check-in, issuance request và cột lease/token. Chạy lại migration an toàn. Chưa coi các test chức năng này là số liệu tải lớn; benchmark và mục tiêu latency được đánh giá ở giai đoạn 8–10.

Tham chiếu triển khai: [RabbitMQ publisher confirms](https://www.rabbitmq.com/docs/confirms), [QRCoder](https://github.com/Shane32/QRCoder).
