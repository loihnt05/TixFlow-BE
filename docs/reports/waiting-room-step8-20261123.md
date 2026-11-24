# Waiting Room — đối chứng sau tối ưu khóa

Ngày 23/11/2026. Sáu run dùng image `sha256:30bf000994acb978d6e7cbcc2158d75b822a842cb297a2fc1a03ca0d0eec6200`, cùng hash image/harness, dataset, 100 khách đến trong một giây, 50 vé, budget 5 grant/s, một API 2 CPU/1 GiB. [Audit máy đọc được](../../artifacts/waiting-room/phase8-validation-audit.json) xác nhận `sameInputs=true`, `valid=true`, `latencyPassed=false`.

| Run | Mode | Status p95 (ms) | Hold p95/p99 (ms) | Join→decision p95 (s) | Paid→Ticket p95 (s) |
| --- | --- | ---: | ---: | ---: | ---: |
| 075049792 | Active | 586,22 | 1.394,89 / 2.013,21 | 19,68 | 1,16 |
| 075322524 | Disabled | — | 2.142,46 / 2.628,11 | 2,43 | 4,15 |
| 075417771 | Active | 301,88 | 1.161,17 / 1.796,68 | 19,58 | 1,08 |
| 075527785 | Disabled | — | 1.732,28 / 2.334,53 | 2,34 | 4,06 |
| 075627052 | Active | 216,89 | 1.299,67 / 1.760,01 | 19,47 | 1,06 |
| 075732817 | Disabled | — | 1.965,89 / 2.149,19 | 2,17 | 4,64 |

Cả sáu run đạt 100 join, 50 Hold/Paid/Ticket, 50 conflict hết vé, 0 lỗi kỹ thuật, drop, oversell, duplicate, thiếu vé sau Paid hoặc queue còn lại. Hold p95/p99, join→decision p95 và Paid→Ticket p95 đều dưới ngưỡng đã khóa (3/6/30/15 giây tương ứng). **Status p95 Active run đầu là 586,22 ms, vượt ngưỡng 500 ms; không tuyên bố toàn bộ cổng độ trễ đạt.** Run này được giữ trong audit.

Pilot chẩn đoán trước tập validation `20261123t074144635z-active` có status p95 185,99 ms và Hold p95 1.779,23 ms, bất biến đúng. Sau khi đổi khóa đọc catalog của settlement/TicketDelivery từ `FOR UPDATE` sang `FOR SHARE`, nhóm SQL tốn thời gian nhất không còn `events ... FOR UPDATE`; điểm tranh chấp còn lại là cập nhật cùng dòng `ticket_inventories` của loại vé bán hết. Đây là đo local trên máy đang chạy stack phát triển khác, không phải SLA production. Kết quả có cải thiện so với bước 7 nhưng còn dao động ở status.
