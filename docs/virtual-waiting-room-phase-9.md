# Virtual Waiting Room — bước 9: phân tán poll và validation local

Ngày 23/11/2026. Sau khi bước 8 còn một run status p95 vượt ngưỡng, status/Join trả `pollAfterMs` ổn định theo `queueId` trong khoảng 3–5 giây. Frontend và k6 dùng chính giá trị server trả, cộng jitter 0–500 ms. Test xác nhận khoảng và cùng interval qua Join/Status.

[Ba cặp đối chứng mới](reports/waiting-room-step9-20261123.md) dùng cùng image `sha256:555ebf904e...`; audit ghi `sameInputs=true`, `valid=true`, `latencyPassed=true`. Mỗi run có 100 join, 50 Hold/Paid/Ticket, 50 conflict hết vé, không lỗi kỹ thuật, drop, oversell hoặc thiếu vé. Status p95 ở ba run Active **246 / 187 / 56 ms**; Hold p95 **1.523 / 1.190 / 287 ms**; join→decision p95 dưới 20 giây. Mọi run qua ngưỡng local đã khóa, gồm Paid→Ticket p95 ≤15 giây. Kết quả bước 8 có run fail vẫn được giữ để thấy độ dao động trước thay đổi.

API quản trị policy, audit và metric/alert từ [bước 8](virtual-waiting-room-phase-8.md) vẫn là đường vận hành. [Runbook](virtual-waiting-room-operations.md) mô tả chuyển trạng thái. Đây là validation local, chưa phải SLA production; cần diễn tập failover Redis/mạng và rollout trên môi trường triển khai trước khi bật policy cho khách thật. Mặc định `Disabled`.

[Soak 10 phút trên cùng image](reports/waiting-room-soak-20261123.md) cũng PASS: 1.000 join/grant, 50 Hold/Paid/Ticket, 950 conflict hết vé, không lỗi kỹ thuật/drop/oversell/thiếu vé và queue rỗng. Backend regression cuối đạt **139/139**, bao gồm test `Paused` vẫn ghi được khi Redis mất kết nối và `Active` bị từ chối. OpenAPI runtime cuối có 58 path; 8 alert rule đã qua `promtool check rules`.
