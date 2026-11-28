# Virtual Waiting Room — bước 3: cấp lượt và Hold Gate

Ngày triển khai: 22/11/2026. Đây là ảnh chụp trạng thái **sau bước 3**; xem [bước 4](virtual-waiting-room-phase-4.md) cho giao diện và xử lý hết vé. Khi policy của suất diễn là `Active`, API nhận khách vào hàng Redis. Controller trên mỗi API replica cùng dùng budget Redis theo giây và cấp lượt FIFO cho entry còn hiệu lực. `Paused` dừng cấp lượt mới nhưng vẫn yêu cầu admission token khi tạo Hold. `Closed` chặn join và Hold mới; kết quả Hold đã commit vẫn replay được. Policy thiếu hoặc `Disabled` giữ luồng đặt vé trực tiếp.

## Luồng API

1. Customer gọi `POST /api/v1/waiting-room/join` với `eventSessionId`, nhận `queueId` nếu policy `Active`/`Paused`.
2. Customer gọi `GET /api/v1/waiting-room/status?queueId={id}` theo `pollAfterMs`. Khi `status` là `Admitted`, response `data.admissionToken` chứa token được bảo vệ bằng ASP.NET Data Protection. Token gắn với `sub`, suất diễn, queue, grant, nonce và thời hạn 2 phút. Chỉ chủ entry đọc được status/token. Không gửi token trong URL.
3. Customer gọi `POST /api/v1/holds` với `Idempotency-Key` và `X-Admission-Token`. Gate kiểm tra token và Redis ràng buộc grant với hash của key và **cùng canonical Hold request** mà Booking dùng cho idempotency. Dùng lại grant cho key/request khác trả `409 ADMISSION_INTENT_CONFLICT`. Token thiếu/sai/hết hạn trả `WAITING_ROOM_REQUIRED`/`ADMISSION_INVALID`/`ADMISSION_EXPIRED`.
4. Retry cùng key và payload sau khi Booking đã commit được replay từ PostgreSQL, kể cả khi grant đã hết hạn. Redis không sẵn sàng khi cần xác thực grant mới: `503 WAITING_ROOM_UNAVAILABLE` và `Retry-After: 3`; không tạo Hold mới. Booking vẫn kiểm tra bán vé, giá, ghế và tồn kho sau Gate.
5. Assistant `confirm-hold` chấp nhận thêm `admissionToken` trong body và chuyển nó tới `POST /holds` qua `X-Admission-Token`. Với sale không bật Waiting Room, trường này có thể bỏ qua.

Token dùng key ring Data Protection hiện được chia sẻ với Assistant, nhưng có purpose riêng `TixFlow.WaitingRoom.Admission.v1`; các API replica cần cùng key ring như cấu hình deployment hiện tại. `Cache-Control: no-store` áp dụng cho route Waiting Room; không ghi token vào log.

## Kiểm tra

`dotnet test TixFlow.sln -c Release --no-restore` dùng Testcontainers PostgreSQL/Redis/RabbitMQ. Integration test kiểm tra token đúng chủ, Gate chặn thiếu token, bind một intent, replay Hold, budget chung dưới các lời gọi đồng thời và `Paused` không grant. Migration `014_waiting_room_policies.sql` phải được chạy trước khi API mới dùng policy.

## Ranh giới hiện tại

Chưa có route quản trị để bật/sửa policy và audit thay đổi. UI phòng chờ, reconnect, sold out, rate limit polling, thử nghiệm đa replica/Redis restart và load profile offered-load vẫn ở bước sau. Vì vậy policy tiếp tục mặc định `Disabled`; không dùng kết quả unit/integration test để kết luận đã chịu được đợt mở bán lưu lượng cao. Controller đọc policy từ PostgreSQL theo chu kỳ 250 ms; thay đổi trạng thái bằng SQL trực tiếp có thể có độ trễ đến chu kỳ kế tiếp và không phải quy trình vận hành được hỗ trợ.
