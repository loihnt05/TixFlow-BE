# Virtual Waiting Room — bước 8: quản trị và giảm tranh chấp khóa

**Báo cáo tại thời điểm bước 8.** Run status p95 vượt ngưỡng được giữ nguyên bên dưới. [Bước 9](virtual-waiting-room-phase-9.md) ghi lại tập validation mới sau khi phân tán lịch polling; không thay thế hoặc xóa kết quả bước 8.

Ngày 23/11/2026. Bước này đổi khóa đọc catalog trong settlement và TicketDelivery sang `FOR SHARE`, thêm API quản trị policy có version/audit, cache status phản ánh policy `Closed`/`Disabled`, cùng metric và alert cho controller/queue. [Runbook vận hành](virtual-waiting-room-operations.md) mô tả cách bật, tạm dừng và xử lý sự cố.

## Mã và kiểm thử

`PUT /api/v1/management/event-sessions/{sessionId}/waiting-room-policy` chỉ cho Admin hoặc Organizer sở hữu sự kiện, yêu cầu `expectedVersion` và lý do 10–500 ký tự. Cập nhật policy và `audit_logs` commit cùng transaction; request dùng phiên bản cũ nhận `409`. `Active` đòi session `OnSale`, event `Published` và Redis sẵn sàng. `Paused`/`Closed` vẫn ghi được khi Redis không kết nối; `queueDepth` trả `null` nếu không đọc được. Migration `015_waiting_room_policy_version.sql` thêm version cho policy đã tồn tại.

Settlement và TicketDelivery chỉ đọc event/session; dùng shared lock vẫn chặn cập nhật catalog trong transaction, đồng thời cho phép các order khác nhau xử lý song song. Pilot `20261123t074144635z-active` qua bất biến, status p95 185,99 ms, Hold p95 1.779,23 ms. Thống kê SQL không còn `events ... FOR UPDATE` trong nhóm truy vấn chờ nhiều; cập nhật dòng inventory chung vẫn là điểm tranh chấp.

Bộ test backend đầy đủ đạt **138/138**, gồm test quyền quản trị, version conflict, audit và phản ánh `Closed` cho entry cũ. Các test chịu lỗi Redis restart, đa replica, Gate fail closed từ bước 5 vẫn được chạy lại.

## Validation sáu run

[Báo cáo](reports/waiting-room-step8-20261123.md) và [audit](../artifacts/waiting-room/phase8-validation-audit.json): sáu run cùng image `sha256:30bf000...`, `sameInputs=true`, `valid=true`, `latencyPassed=false`. Mọi run đạt 100 join, 50 Hold/Paid/Ticket, 50 conflict hết vé và không sai lệch inventory. Hold p95 Active 1.161–1.395 ms, p99 1.760–2.013 ms; Paid→Ticket p95 1.060–1.158 ms; join→decision p95 dưới 20 giây. Status p95 Active là **586 / 302 / 217 ms**, nên run đầu vượt ngưỡng 500 ms. Không loại run đầu hoặc đổi ngưỡng sau khi nhìn kết quả.

## Còn mở

[Soak local 10 phút](reports/waiting-room-soak-20261123.md) với 1.000 khách rải đều đã PASS: 1.000 join/grant, 50 Hold/Paid/Ticket, 950 conflict hết vé, 0 lỗi kỹ thuật/drop/oversell/thiếu vé và queue rỗng. Nó xác minh sự ổn định ở 1,67 khách/giây, không thay thế flash crowd 100 khách/giây. OpenAPI runtime đã xuất lại với 58 path; `promtool` xác thực cả 8 alert rule.

Tập flash-crowd validation local chưa qua toàn bộ cổng độ trễ. Máy đo có stack phát triển khác chạy đồng thời; cần lặp lại campaign độc lập trên tài nguyên tách biệt rồi đánh giá status p95 ở từng run. Fault injection Redis/mạng và diễn tập rollout trên môi trường triển khai thực tế vẫn là các cổng vận hành còn phải hoàn thành. Policy production vẫn mặc định `Disabled`.
