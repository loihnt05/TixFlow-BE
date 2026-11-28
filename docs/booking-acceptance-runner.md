# Phase 05 — Runner nghiệm thu mua vé

Runner Node.js tạo giao dịch thật qua API của **môi trường nghiệm thu cách ly**. Yêu cầu Node 22+, PostgreSQL/Worker/broker/API/Keycloak đang chạy, hai Customer khác nhau và fixture GA còn vé, không có tiến trình khác ghi vào cùng fixture. Waiting Room của session phải Disabled trong lượt kiểm tra này; runner chưa thực hiện admission. Không chạy lên môi trường live.

Đặt JWT test trong `TIXFLOW_CUSTOMER_A_TOKEN`, `TIXFLOW_CUSTOMER_B_TOKEN`; nếu kiểm tra check-in, thêm `TIXFLOW_ORGANIZER_A_TOKEN` của chủ event. Không đặt JWT trong tham số CLI hoặc Git.

```powershell
node scripts/Run-BookingAcceptance.mjs --execute --base-url http://localhost:8080 --event <event-uuid> --session <session-uuid> --ticket <ticket-type-uuid> --scenario purchase
```

`--scenario` nhận `purchase`, `cancel`, `failed`, `timeout`. Mỗi lượt sinh run ID mới và giao dịch mới; dùng fixture đủ vé hoặc DB nghiệm thu mới cho lượt tiếp theo. Runner không tự xóa giao dịch sau thất bại. ID Hold/Order/Payment/Ticket đã nhận được lưu trong báo cáo để truy vết; nếu request timeout trước khi nhận ID, đối chiếu server bằng idempotency key `<runId>:hold`, `:checkout`, `:payment`.

`purchase` kiểm tra Hold/checkout/payment replay với cùng idempotency key, tổng tiền/currency, payment thành công, Worker phát đúng một vé và Customer B không đọc được vé/QR của A. `failed` và `timeout` yêu cầu payment kết thúc ở Failed, order không Paid và chưa có vé tại thời điểm kiểm tra. `cancel` kiểm tra hủy Hold lặp trả cùng kết quả và tồn kho trở về giá trị trước lượt chạy.

Thêm `--checkin` cho `purchase` khi session bắt đầu trong 60 phút tới. Runner kiểm tra cửa kiểm vé và quyền Organizer trước khi tạo Hold, dùng QR trong bộ nhớ, xác nhận check-in replay giữ timestamp và scan mới trả `TICKET_ALREADY_USED`. Không bật bypass cửa kiểm vé.

Báo cáo được ghi cả khi lỗi tại `artifacts/acceptance/booking-<runId>/report.json`, gồm stage, ID, route và mã HTTP. Không lưu token, QR hoặc response body. Polling giới hạn 30 lần, mỗi lần cách một giây và mỗi HTTP request timeout 15 giây; không tự retry mutation khi mất kết nối. `status=passed` chỉ chứng minh kịch bản đã chọn, chưa thay cho browser E2E, reserved seats, tranh vé, expiry, restart, Waiting Room hay đo oversold trên DB.

Kiểm tra logic runner ngoại tuyến:

```powershell
node --test tests/booking-acceptance.test.mjs
```

12 test giả lập đã đạt ở lượt triển khai. Chưa chạy runner với hệ thống thật vì Docker daemon không khả dụng. Các mục APP-07..20 vẫn cần Actual/evidence từ môi trường thật trước khi đóng Phase 05.
