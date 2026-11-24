# Virtual Waiting Room — bước 2: hàng chờ Redis và policy

Ngày triển khai: 22/11/2026. Đây là ảnh chụp trạng thái **sau bước 2**. Bước 3 đã bổ sung Gate và controller; xem [trạng thái hiện tại](virtual-waiting-room-phase-3.md).

## Đã có trong runtime

- Migration `014_waiting_room_policies.sql` tạo policy theo suất diễn, mặc định `Disabled`, với các trạng thái `Disabled`, `Active`, `Paused`, `Closed` và `max_grants_per_second` từ 1 đến 1000. Chưa có API quản trị để bật policy; phải hoàn thành Gate trước khi mở tính năng cho người dùng.
- Redis giữ entry, ánh xạ một `(sub, eventSessionId)` tới một entry còn hiệu lực, thứ tự FIFO, trạng thái và thời hạn. Lua script làm join/rejoin, heartbeat/status, abandon và lấy người kế tiếp nguyên tử. Entry `Waiting` hết hạn sau 5 phút không join/status; lần cấp lượt nội bộ có thời hạn 2 phút. Trạng thái kết thúc được giữ tối đa 24 giờ để tra cứu; queue/sequence hết hạn sau 7 ngày không hoạt động.
- `POST /api/v1/waiting-room/join` yêu cầu Customer và `{ "eventSessionId": "uuid" }`. Khi không có policy hoặc policy `Disabled`, trả `200 {"data":{"mode":"Direct"}}`. Với `Active`/`Paused`, trả `mode: "Waiting"`, `queueId`, `status`, `positionEstimate`, `pollAfterMs: 3000`. Gọi lại cùng tài khoản/suất diễn trả entry đang hoạt động. `Closed` trả 409.
- `GET /api/v1/waiting-room/status?queueId=...` yêu cầu đúng chủ entry và trả trạng thái, vị trí ước lượng, thời hạn, `grantId` nội bộ, `pollAfterMs`; `admissionToken` hiện luôn `null`. `DELETE /api/v1/waiting-room/{queueId}` đánh dấu bỏ hàng và trả 204 khi đúng chủ entry. Cả nhóm route đặt `Cache-Control: no-store`.
- Nếu Redis lỗi khi truy cập hàng chờ, route trả `503 WAITING_ROOM_UNAVAILABLE` và `Retry-After: 3`. Mode `Direct` đọc policy từ PostgreSQL và không cần kết nối Redis.

## Cách kiểm tra

Chạy migration theo quy trình hiện tại, bật PostgreSQL/Redis, rồi dùng Bearer token Customer:

```http
POST /api/v1/waiting-room/join
Content-Type: application/json
Authorization: Bearer <customer-token>

{"eventSessionId":"<published-event-session-id>"}
```

Khi policy chưa được tạo, kết quả mong đợi là `mode: "Direct"`. Integration test dùng database và Redis cách ly để tạo policy `Active`, sau đó kiểm tra join lặp, quyền chủ sở hữu, bỏ hàng và cấp lượt FIFO. Chạy lại bằng `dotnet test TixFlow.sln -c Release --no-restore` sau khi đã restore package và Docker sẵn sàng.

## Phần còn thiếu

Controller chưa cấp lượt theo `max_grants_per_second`; primitive `GrantNextAsync` chỉ được gọi trong test. Chưa ký admission token, chưa chặn Hold thiếu token, chưa ràng buộc token với idempotency/Assistant, chưa có UI phòng chờ hoặc load profile mới. Vì vậy không nên chỉnh policy trực tiếp sang `Active` trên môi trường có khách thật: họ có thể vào hàng nhưng không có đường tiếp tục tạo Hold qua hàng chờ. Bước kế tiếp là Gate và controller, sau đó mới thêm route quản trị để bật policy có kiểm soát.
