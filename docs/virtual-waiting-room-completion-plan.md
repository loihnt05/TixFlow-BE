# Kế hoạch hoàn thiện Virtual Waiting Room

Ngày đối chiếu: 27/11/2026. Nguồn yêu cầu là tài liệu `Virtual Waiting Room In.docx` do người dùng cung cấp. Các ví dụ 5.000 khách và 500 người active trong tài liệu là giả định, chưa phải kết quả đo. Trạng thái bên dưới được kiểm tra với mã nguồn và báo cáo validation trong repository.

## Đã có bằng chứng trong repository

| Năng lực | Bằng chứng | Giới hạn hiện tại |
| --- | --- | --- |
| FIFO và nhiều API replica | Redis `INCR`, Sorted Set, Lua grant; `WaitingRoomResilienceTests` | Chưa đo flash crowd 5.000 khách |
| Join, reconnect, token và Hold gate | API waiting room, frontend `useWaitingRoom`, `BookingModule` | Gate chỉ áp dụng khi policy khác `Disabled` |
| Giới hạn grant theo giây | Redis budget dùng chung giữa replica | Tốc độ do quản trị viên cấu hình, chưa tự điều chỉnh |
| Monitoring | Prometheus metrics, Grafana và 8 alert rule | Chưa xác nhận vận hành production |
| Benchmark local | Báo cáo bước 9: ba cặp đối chứng hợp lệ; soak 10 phút với 1.000 join | Chưa đủ tải 5.000 join, 1.000 admission hoặc ba replica trên môi trường triển khai |

## WR Phase 10 — Giới hạn lượt admission đồng thời

**Đã triển khai trong mã ở lần đối chiếu này:** migration `016_waiting_room_admission_capacity.sql` thêm `maxConcurrentAdmissions` mặc định 500 vào policy; API quản trị đọc/ghi và audit trường này; Lua grant dùng Sorted Set Redis để đếm các grant chưa dùng còn hiệu lực, dọn lease hết hạn và chỉ cấp lượt khi dưới capacity. Các đường consumed, abandoned, rejected và expired giải phóng slot. Có test tranh chấp 12 request với capacity 2, hai API replica dùng chung capacity 3, và tái cấp sau khi consume/abandon/expiry.

**Tiêu chí nghiệm thu:** build backend sạch; test tích hợp trên Redis/PostgreSQL chạy qua; nhiều replica không cấp quá capacity; FIFO vẫn đúng; khi Redis lỗi không cấp lượt mới. Trạng thái: **mã hoàn tất, test tích hợp chưa chạy được trên máy này vì Docker daemon không hoạt động**.

Capacity này là số **admission grant chưa dùng**, tối đa trong cửa sổ token 2 phút. Nó không phải tổng số người đang ở checkout. Việc tạo Hold giải phóng lease, nên không được ghi `ActiveUsers ≤ Capacity` cho toàn bộ hành trình mua vé.

## WR Phase 11 — Phiên mua vé active và vòng đời slot

Thiết kế trạng thái `Admitted → Active → Completed/Expired/Cancelled` theo từng suất diễn và user. Chốt thời điểm chuyển sang Active khi tạo Hold; gia hạn có giới hạn bằng heartbeat hoặc hoạt động nghiệp vụ; giải phóng đúng một lần khi checkout hoàn tất, hủy Hold, hết TTL hoặc payment hết hạn. Ràng buộc slot với Hold/Order, không tin client heartbeat để giữ slot vô hạn. Dùng Redis Lua để cấp/giải phóng nguyên tử giữa replica và đối soát với dữ liệu PostgreSQL sau restart.

**Tiêu chí nghiệm thu:** test tranh chấp nhiều replica chứng minh `ActiveUsers ≤ Capacity`; test token dùng lặp, mất heartbeat, expiry, hủy, checkout và restart không rò slot hoặc giải phóng slot của phiên khác. Trạng thái: **chưa triển khai**.

## WR Phase 12 — Điều tiết động và chống lạm dụng

Dùng p95 Hold, tỷ lệ 429/5xx và tín hiệu pool/DB/worker để tính admission budget theo cửa sổ. Quy định ngưỡng giảm, tăng dần, hysteresis, dữ liệu metric quá cũ và `budget = 0` khi hệ thống suy giảm nghiêm trọng. Lưu quyết định/metric để giải thích từng lần thay đổi. Bổ sung challenge/CAPTCHA phù hợp cho dấu hiệu bot, chính sách nhiều tài khoản và giới hạn tại edge; kiểm tra trực tiếp API không vượt được gate.

**Tiêu chí nghiệm thu:** fault injection làm budget giảm hoặc dừng, phục hồi tăng dần; tải bình thường không dao động; thử replay, account abuse và bypass API. Trạng thái: **chưa triển khai**.

## WR Phase 13 — Đo tải, diễn tập sự cố và rollout

Chạy k6 với cùng dữ liệu và hạ tầng cho hai cấu hình Waiting Room Active/Disabled: 5.000 join đồng thời, 1.000 lượt cấp quyền, ba API replica. Thu p50/p95/p99, throughput, error, queue wait, FIFO, oversell, CPU/RAM và số slot active. Diễn tập Redis kill/restart, đứt mạng, controller failover và PostgreSQL suy giảm; lưu log, cấu hình, raw result và ngưỡng pass/fail trước khi chạy. Chỉ bật policy production sau khi các cổng nghiệm thu và rollback thực tế qua.

**Tiêu chí nghiệm thu:** báo cáo tái lập được, không vượt capacity, không oversell, FIFO đúng theo chính sách, không bypass lúc Redis lỗi, độ trễ đạt ngưỡng đã khóa. Trạng thái: **chưa có bằng chứng cho quy mô này**.

## Hướng mở rộng sau nghiệm thu

Randomized pre-queue, SSE/WebSocket và Redis Pub/Sub là lựa chọn sản phẩm, không phải điều kiện để FIFO polling hiện tại hoạt động. Chỉ triển khai khi có yêu cầu công bằng trước giờ mở bán hoặc phép đo chứng minh polling không đáp ứng được tải mục tiêu.
