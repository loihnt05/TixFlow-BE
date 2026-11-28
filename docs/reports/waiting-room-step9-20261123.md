# Waiting Room — validation sau phân tán poll

Ngày 23/11/2026. Sáu run dùng image `sha256:555ebf904e796f16843fc9546b8dc526cdbeaaeaeb78fb407368bbe5718fde71`, cùng source/harness/dataset/tài nguyên: 100 Customer bắt đầu join trong một giây, 50 vé, budget 5 grant/s, một API 2 CPU/1 GiB và PostgreSQL 1 CPU/512 MiB. [Audit máy đọc được](../../artifacts/waiting-room/phase9-validation-audit.json) xác nhận `sameInputs=true`, `valid=true`, `latencyPassed=true`.

| Run | Mode | Status p95 (ms) | Hold p95/p99 (ms) | Join→decision p95 (s) | Paid→Ticket p95 (s) |
| --- | --- | ---: | ---: | ---: | ---: |
| 081547062 | Active | 246,07 | 1.523,02 / 2.252,90 | 19,33 | 2,25 |
| 081819579 | Disabled | — | 1.511,38 / 2.134,66 | 2,27 | 4,11 |
| 081919220 | Active | 187,05 | 1.189,78 / 2.233,80 | 19,44 | 1,61 |
| 082028042 | Disabled | — | 1.628,94 / 1.836,78 | 2,25 | 5,13 |
| 082124525 | Active | 55,99 | 286,86 / 482,97 | 19,99 | 1,10 |
| 082237594 | Disabled | — | 1.718,47 / 1.934,21 | 2,36 | 2,66 |

Cả sáu run có 100 join, 50 Hold/Paid/Ticket và 50 conflict hết vé; không lỗi kỹ thuật, iteration drop, oversell, duplicate, thiếu vé sau Paid hoặc queue còn lại. **Từng** run Active qua status p95 ≤500 ms, Hold p95/p99 ≤3.000/6.000 ms và join→decision p95 ≤30 giây. Paid→Ticket p95 ≤15 giây ở cả hai mode. Ngưỡng và phép audit đã khóa trước khi chạy tập này.

Thay đổi duy nhất trên đường status so với [tập trước](waiting-room-step8-20261123.md) là `pollAfterMs` ổn định theo `queueId` trong khoảng 3–5 giây. Client frontend và k6 đều tôn trọng khoảng này, cộng jitter 0–500 ms. Nó phân tán đợt poll đầu của flash crowd thay vì dồn tại cùng mốc ba giây. Tập trước có một run status p95 586 ms và được giữ nguyên, không thay bằng run mới trong audit.

Đây là cổng **local** trên máy còn chạy stack phát triển, không phải SLA production hay bằng chứng failover nhiều máy. Bật policy cho khách thật vẫn cần diễn tập môi trường triển khai, kiểm tra lỗi mạng/Redis và theo dõi metric trong [runbook](../virtual-waiting-room-operations.md).
