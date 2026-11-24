# Virtual Waiting Room — bước 4: giao diện phòng chờ

Ngày triển khai: 22/11/2026. Đây là ảnh chụp trạng thái **sau bước 4**; xem [bước 5](virtual-waiting-room-phase-5.md) cho test đa replica và run k6. Checkout và Assistant dùng chung client `useWaitingRoom` để gọi `join → status → Hold`. Với policy `Disabled`/không có policy, `join` trả `Direct` và giao diện đi tiếp như cũ. Với `Active`/`Paused`, nút tạo Hold chờ đến khi status `Admitted` và gửi `X-Admission-Token`.

## Hành vi người mua

- Checkout tự vào phòng chờ sau khi Customer đăng nhập và có `eventSessionId`. `queueId` được giữ trong `sessionStorage` theo `sub` và suất diễn; tải lại trang đọc status để nối lại đúng entry. Admission token chỉ nằm trong bộ nhớ, lấy lại qua status với Bearer token; không đưa vào URL hoặc storage.
- Polling theo `pollAfterMs` của server, giới hạn 3–5 giây cộng jitter tối đa 500 ms. Lỗi tạm thời tôn trọng `Retry-After` và tự thử lại; hết hạn có nút vào lại hàng chờ, khách được xếp ở cuối hàng. Người mua có thể rời hàng qua API `DELETE`.
- Panel hiển thị vị trí **ước lượng**, thời hạn grant và thông báo rằng được cấp lượt không bảo đảm còn vé. Đồng hồ giữ vé chỉ hiển thị sau khi Hold tồn tại.
- Nếu phản hồi Hold bị mất sau khi server commit, checkout giữ `Idempotency-Key` và cho thử lại cùng intent để nhận replay từ PostgreSQL, kể cả khi grant đã tiêu thụ. Sau payment thành công, key checkout cũ được xóa để lần mua sau tạo intent mới.
- Assistant bắt đầu theo dõi phòng chờ khi có đề xuất Hold. Nếu đề xuất xác nhận hết hạn trong lúc chờ, người mua làm mới nó qua `prepare-hold` **sau khi được cấp lượt**, rồi mới xác nhận. Retry kết quả xác nhận dùng cùng confirmation token khi còn hiệu lực.
- Backend từ chối join mới khi suất diễn `SoldOut`/đóng; status của entry đang `Waiting`/`Admitted` chuyển sang `Rejected` để UI dừng chờ. Booking vẫn là nơi quyết định tồn kho khi tạo Hold.

## Kiểm tra và giới hạn

`npm run lint` kiểm tra TypeScript frontend; `npm run build` tạo production artifact bằng Webpack. Trang chủ được đánh dấu dynamic vì nó đọc catalog `no-store` theo request. Integration test Waiting Room kiểm tra `Rejected` khi suất chuyển `SoldOut`.

Chưa có API quản trị để bật/audit policy và chưa chạy profile tải mới. Assistant không khôi phục đề xuất chưa xác nhận sau khi tải lại chat; khách có thể yêu cầu đề xuất mới, còn queue ID vẫn tồn tại trong sessionStorage theo suất diễn. Bước tiếp theo là test đa replica, Redis/controller restart và load profile riêng trước khi bật tính năng vận hành.
