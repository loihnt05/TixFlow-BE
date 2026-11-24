# Waiting Room — run Active đầu tiên (23/11/2026)

Run hợp lệ: `20261123t022616817z-active`. Raw, summary, manifest, Compose đã render, invariant và log nằm tại `artifacts/waiting-room/20261123t022616817z-active/`. Image ứng viên được build từ workspace trong run; `manifest.json` giữ image ID, revision, trạng thái dirty và SHA-256 của source/harness.

| Cấu hình | Giá trị |
| --- | --- |
| Offered load | 100 Customer khác nhau gửi join trong 1 giây; 130 VU được cấp sẵn, tối đa 100 request HTTP join |
| Policy | `Active`, tối đa 5 grant/giây, sale 50 vé GeneralAdmission |
| API | Hai replica, mỗi replica 1 CPU/512 MiB, pool tối đa 40; Nginx chia tải |
| PostgreSQL / Redis | 1 CPU/512 MiB / 0,5 CPU/256 MiB; Redis AOF trên bind mount riêng |
| Luồng | join → status → admitted → hold/replay → checkout → mock payment Paid → ticket |

| Kết quả | Giá trị |
| --- | ---: |
| Join / Admitted | 100 / 100 |
| Hold tạo / conflict hết vé | 50 / 50 |
| Paid / Ticket | 50 / 50 |
| Lỗi kỹ thuật / dropped iteration / check thất bại | 0 / 0 / 0 |
| Oversell, inventory mismatch, duplicate Hold/Order/Payment/Ticket, thiếu vé Paid | 0 |
| Queue sau run | 0 |
| p95/p99 `GET status` | 898,77 / 2.991,09 ms |
| p95/p99 `POST holds` sau Gate | 3.719,14 / 4.437,81 ms |
| p95 `join→terminal decision` | 20,39 giây |

Run này đạt cổng **tính đúng** của harness, nhưng p95 status cao hơn mục tiêu 500 ms và p95 Hold cao hơn mục tiêu 3.000 ms nêu ở hợp đồng bước 1. Profile dùng hai API replica thay vì một API 2 CPU/1 GiB nên chưa được chấm là pass/fail SLO mục tiêu. Chưa có cặp Disabled cùng cấu hình và chưa có ba run độc lập; chưa kết luận Waiting Room cải thiện hay làm giảm hiệu năng tổng thể.

Run đầu `20261123t022232725z-active` **không hợp lệ cho cổng load**: k6 drop 1 iteration ở ranh giới 100 VU, dù 100 request đã hoàn thành và bất biến PostgreSQL đều bằng 0. Sau đó harness được tăng VU dự phòng và giới hạn đúng 100 HTTP arrival; không dùng run đầu để tính hiệu năng validation.
