# Giai đoạn 6 — Checkout, Order và payment

## Phạm vi đã triển khai

- Checkout tạo đúng một Order cho mỗi Hold; request lặp lại không nhân đôi Order/OrderItem. Giá, loại vé, tiền tệ và phí được lấy từ snapshot Hold, không tin tổng tiền từ browser.
- HoldItem và OrderItem giữ tên loại vé/giá/phí snapshot. Hiện chưa có chính sách phí dịch vụ được duyệt nên phí được chụp thành `0 VND`.
- Mock payment tạo attempt `Pending` và outbox message trong một transaction. Simulator chạy sau commit, không giữ transaction trong lúc chờ; hỗ trợ `Succeeded`, `Failed`, `Timeout` (timeout mô phỏng được ghi là `Failed/MOCK_TIMEOUT`).
- Callback chỉ chấp nhận Bearer JWT audience `tixflow-mock-callback` với scope `payment:callback`. Human token không được phép gọi route này.
- Webhook event có unique key `(provider, provider_event_id)`. Cùng event/payload trả `Duplicate`; event ID dùng lại với payload khác không chạy settlement lần hai.
- Settlement khóa Event → Session → Hold → Order → Payment, lấy giờ từ PostgreSQL và cập nhật Payment, Order, Hold, inventory counters cùng seat allocations trong một transaction. Callback tới sau deadline/hủy Event được xử lý xác định và inventory được release.
- UI checkout gửi Hold/checkout/payment API; trang kết quả đọc trạng thái Order từ server và lịch sử tài khoản tải từ `GET /orders`. QR/e-ticket chưa hiển thị như vé đã phát hành; phát hành vé thuộc Giai đoạn 7.

## API

| Method | Endpoint | Quyền | Ý nghĩa |
| --- | --- | --- | --- |
| `POST` | `/api/v1/holds/{holdId}/checkout` | Customer | Tạo Order từ Hold; uniqueness tự nhiên theo Hold |
| `GET` | `/api/v1/orders` | Customer/Admin | Lịch sử đơn hàng trong phạm vi |
| `GET` | `/api/v1/orders/{orderId}` | Customer/Admin | Đọc trạng thái và snapshot Order |
| `POST` | `/api/v1/orders/{orderId}/payments/mock` | Customer | Tạo attempt và simulator outbox |
| `GET` | `/api/v1/payments/{paymentId}` | Customer/Admin | Poll trạng thái Payment |
| `POST` | `/api/v1/payment-webhooks/mock` | Mock service | Nhận callback riêng cho dịch vụ |

Checkout cần `Idempotency-Key` và `X-Expected-Version` Hold khi tạo mới. Payment cần key và version Order. Khi Hold đã có Order, API trả Order hiện có và không kiểm tra lại version/deadline. Mỗi Order chỉ có tối đa một Payment đang Pending.

## Migration và kiểm thử

Migration `008_phase6_checkout_payments.sql` bổ sung version Order, tên/phí snapshot, mock outcome và metadata webhook. Áp dụng sau migration 007.

`CheckoutPaymentTests` kiểm tra checkout/payment replay cùng status/body gốc, replay tự nhiên theo Hold, thanh toán thành công, timeout và retry, duplicate webhook, và ranh giới quyền callback. Bộ backend tổng thể hiện có 40 test.

## Lưu ý vận hành

Simulator hiện là hosted service trong API và dùng PostgreSQL outbox làm hàng đợi bền vững; callback HTTP độc lập được bảo vệ bằng service JWT. Realm mẫu khai báo service account `tixflow-mock-callback` với audience/scope callback riêng; quản trị viên cần cấp và bảo vệ client secret khi tích hợp provider ngoài. Payment giả lập không thu tiền thật. Ticket generation, QR và check-in tiếp tục thuộc Giai đoạn 7.
