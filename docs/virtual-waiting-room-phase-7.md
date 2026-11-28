# Virtual Waiting Room — bước 7: tìm nút thắt độ trễ

Ngày 23/11/2026. Đây là ảnh chụp sau bước 7; xem [bước 8](virtual-waiting-room-phase-8.md) cho API quản trị, audit, metric và phép đo sau tối ưu khóa. Bước này bổ sung timing có thể bật riêng cho benchmark, giảm chi phí đồng bộ user ở đường request phổ biến, và cache trạng thái mở bán trong 100 ms để gộp các status poll đồng thời. Policy mặc định vẫn là `Disabled`.

## Kết quả

[Audit sáu run](../artifacts/waiting-room/phase7-sale-cache-audit.json) dùng cùng image `sha256:01444a1b...`, cùng input và tài nguyên: ba `Active`, ba `Disabled`, mỗi run 100 khách đến trong một giây, budget 5 grant/s, 50 vé. Cả sáu run qua cổng tính đúng: 100 join, 50 Hold/Paid/Ticket, 50 conflict hết vé, không lỗi kỹ thuật, drop, bán vượt hoặc thiếu vé; queue còn 0.

| Mode | Status p95 (ms) | Hold p95 (ms) | Hold p99 (ms) | Quyết định p95 (s) |
| --- | ---: | ---: | ---: | ---: |
| Active 1 | 290,20 | 2.953,87 | 3.463,96 | 21,72 |
| Active 2 | 269,22 | 2.922,25 | 3.216,89 | 18,80 |
| Active 3 | 1.067,50 | 3.005,23 | 3.616,02 | 19,97 |
| Disabled 1–3 | — | 4.235,18 / 4.179,16 / 5.513,14 | 5.344,16 / 5.637,16 / 6.178,69 | 6,12 / 6,31 / 9,39 |

**Chưa qua cổng độ trễ đã khóa ở bước 6.** Status p95 phải ≤500 ms và Hold p95 ≤3.000 ms ở từng run Active; lần thứ ba vượt cả hai (Hold vượt 5,23 ms). Hold p99 ≤6.000 ms và join→decision p95 ≤30 giây đều đạt. Không bỏ run thứ ba. Máy local có stack phát triển TixFlow khác chạy đồng thời trong giai đoạn này, nên không quy toàn bộ chênh lệch so với bước 6 cho thay đổi code.

Run chẩn đoán riêng `20261123t072402126z-active` bật `WaitingRoom:DiagnosticsEnabled`: status p95 256,03 ms, Hold p95 1.309,57 ms, toàn bộ bất biến đúng. [Timing từng stage](../artifacts/waiting-room/20261123t072402126z-active/timings.json): status identity p95 139,39 ms, Redis 28,48 ms, truy vấn trạng thái sale 93,81 ms; Hold sau identity p95 1.193,04 ms. [Thống kê PostgreSQL](../artifacts/waiting-room/20261123t072402126z-active/pg-stat-statements.json) ghi hai câu lệnh có tổng thời gian thực thi lớn nhất: `events ... FOR UPDATE` trong settlement (88 calls, tổng 18,43 s, max 881 ms) và `UPDATE ticket_inventories` khi Hold (100 calls, tổng 16,69 s, max 1.341 ms). Đây là bằng chứng điểm nghẽn khóa/ghi cần điều tra tiếp; `pg_stat_statements` không tự chỉ ra toàn bộ chuỗi chờ khóa của một request.

Đồng bộ local user trước đây thực hiện upsert rồi SELECT trên mọi request. Đường phổ biến nay SELECT một lần và chỉ upsert khi claim thay đổi, vẫn đọc status local hiện tại. Cache sale state giới hạn 512 session, TTL 100 ms, gộp truy vấn đồng thời theo session. Nó có thể trả trạng thái còn mở trễ tối đa khoảng 100 ms sau cập nhật catalog; Booking vẫn xác minh trạng thái trong transaction. Test SoldOut kiểm tra cache hết hạn.

## Còn phải làm trước khi bật cho khách thật

1. Đo lock wait/transaction của Hold, settlement và TicketDelivery; sửa điểm đồng bộ trên bản thiết kế khóa nhất quán, rồi chạy lại test tranh chấp, thanh toán, hủy và cấp vé.
2. Chạy lại ít nhất ba cặp Active/Disabled trên máy benchmark không có stack phát triển cạnh tranh tài nguyên. Chỉ chấp nhận khi **từng** run qua status p95 ≤500 ms, Hold p95 ≤3.000 ms, Hold p99 ≤6.000 ms, decision p95 ≤30 s và cổng tính đúng.
3. Bổ sung API quản trị policy và audit, quy trình bật/tắt theo sự kiện, cảnh báo độ sâu queue và Redis, thử soak dài và lỗi mạng/Redis. Đánh giá độ trễ Paid→Ticket rồi khóa ngưỡng trước validation production.

## Chạy lại

`./scripts/Invoke-WaitingRoomLoad.ps1 -Mode Active -Arrivals 100 -GrantsPerSecond 5 -ApiReplicas 1 -Diagnostics` tạo `services.log`, `pg-stat-statements.json`, manifest, k6 summary và invariant trong `artifacts/waiting-room/<run-id>-active/`. Dùng `node scripts/Analyze-WaitingRoomTimings.mjs <thư mục run>` để tính timing từng stage. Tắt `-Diagnostics` cho validation. Benchmark dùng Compose project riêng; RabbitMQ trong overlay không dùng tmpfs để tránh lỗi cookie trên Docker Desktop local.
