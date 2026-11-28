# Waiting Room — soak local 10 phút

Run cuối `20261123t082610556z-active` trên image validation bước 9 `sha256:555ebf904e796f16843fc9546b8dc526cdbeaaeaeb78fb407368bbe5718fde71`: 1.000 Customer rải đều trong 600 giây (1,67/s), policy `Active` cấp tối đa 5 grant/s, một API 2 CPU/1 GiB, PostgreSQL 1 CPU/512 MiB và 50 vé GeneralAdmission. `result.json` báo PASS; k6 hoàn tất trong 604,6 giây.

| Kết quả | Giá trị |
| --- | ---: |
| Join / grant | 1.000 / 1.000 |
| Hold / Paid / Ticket | 50 / 50 / 50 |
| Conflict hết vé | 950 |
| Iteration drop / lỗi kỹ thuật / check fail | 0 / 0 / 0 |
| Oversell / thiếu vé sau Paid / queue còn lại | 0 / 0 / 0 |
| Status p95 / Hold p95 | 5,55 / 18,03 ms |
| Paid→Ticket p95 | 1,02 giây |

Raw k6, invariant PostgreSQL, `pg-stat-statements`, manifest và log ở `artifacts/waiting-room/20261123t082610556z-active/`. Đây là phép thử ổn định ở tốc độ thấp sau một flash sale, không thay thế phép đo 100 join/giây hoặc kiểm thử failover. 50 vé được bán sớm; phần lớn thời gian còn lại kiểm tra đường hết vé và vòng controller. Run trước `20261123t080104930z-active` trên image bước 8 cũng PASS với cùng số đếm, nhưng được giữ riêng vì poll interval khác.

Lần thử trước `20261123t075930103z-active` **không có dữ liệu soak hợp lệ**: k6 exit 137 lúc khởi tạo VU trong giới hạn RAM 1 GiB, trước khi phát arrivals. Harness sau đó giảm dự phòng từ 220 xuống 70 VU cho profile 1,67/s; run hợp lệ dùng cấu hình mới. Hai lần được giữ tách biệt, không trộn số đo.

Chạy lại:

```powershell
./scripts/Invoke-WaitingRoomLoad.ps1 -Mode Active -Arrivals 1000 -ArrivalWindowSeconds 600 -GrantsPerSecond 5 -ApiReplicas 1
```
