# Virtual Waiting Room — bước 5: chịu lỗi và profile tải

Ngày 23/11/2026. Đây là ảnh chụp trạng thái **sau bước 5**; xem [bước 6](virtual-waiting-room-phase-6.md) cho đối chứng ba cặp và OpenAPI runtime. Bước này thêm test hai API host dùng chung PostgreSQL/Redis, restart Redis có AOF, restart một controller replica khi queue đang rút và fail closed khi API không kết nối được Redis. Stack tải riêng thêm Redis, hai API replica và Nginx chia tải; không sửa nhãn hay profile phase 10.

## Chạy lại

Từ `tixflow-backend` trên máy có Docker Desktop và PowerShell:

```powershell
./scripts/Invoke-WaitingRoomLoad.ps1 -Mode Active -Arrivals 100 -GrantsPerSecond 5
./scripts/Invoke-WaitingRoomLoad.ps1 -Mode Disabled -Arrivals 100 -GrantsPerSecond 5
```

Mỗi lệnh build image ứng viên mới từ workspace, tạo Compose project và database riêng, nạp migration/seed, rồi ghi `manifest.json`, `compose.yml`, `raw.json.gz`, `summary.json`, `invariants.json`, `result.json` và log tại `artifacts/waiting-room/<run-id>-<mode>/`. Script hạ **chỉ** Compose project của run đó trong `finally`, không xóa artifact. `Active` được bật bằng SQL chỉ trong database thử nghiệm; không có route quản trị production. k6 phát tối đa 100 request `join` từ 100 Customer khác nhau trong cửa sổ 1 giây, poll status, thử Hold với token, kiểm tra replay cùng intent, rồi checkout → Paid → Ticket. Mode `Disabled` dùng cùng luồng mà không cần grant. k6 giữ VU dự phòng và chặn iteration ở ranh giới 1 giây để giữ đúng số request HTTP đã khai báo.

## Test tính đúng đã thêm

- Hai API host khác service provider cùng nhìn một hàng FIFO và chia sẻ budget Redis; token phát từ host này dùng được qua Gate host kia nhờ key ring chung.
- Redis container dừng/khởi động lại có AOF giữ entry và thứ tự; test tạo kết nối mới tới endpoint host được Testcontainers cấp sau restart.
- Một controller host dừng khi sale `Active`, host thay thế tiếp tục đọc queue; tổng grant trong từng giây không vượt policy.
- API với Redis endpoint không kết nối được trả `503 WAITING_ROOM_UNAVAILABLE` và `Retry-After: 3` cho Hold mới có token, không đi vòng qua Gate.

## Kết quả local

Toàn bộ backend regression suite đạt **136/136** (`dotnet test TixFlow.sln -c Release --no-build`). Xem [báo cáo run 23/11/2026](reports/waiting-room-step5-20261123.md). Run Active hợp lệ có 100 join, 100 grant, 50 Hold, 50 Paid, 50 Ticket, 50 conflict hết vé; 0 lỗi kỹ thuật, 0 iteration drop, 0 oversell/bất biến sai và queue rỗng. Đây là **một** run với **hai** API replica 1 CPU/512 MiB mỗi replica, khác profile mục tiêu một API 2 CPU/1 GiB trong hợp đồng bước 1. p95 status/Hold vượt ngưỡng mục tiêu số; cần đo Disabled và thêm các run độc lập trước khi đánh giá hiệu năng hoặc điều chỉnh controller.

## Còn lại

Chưa thử Redis bị kill đột ngột, mất AOF, partition mạng dài, nhiều replica trên nhiều máy, hoặc soak dài hạn. Không coi restart có chủ đích trong Testcontainers là bằng chứng khôi phục mọi sự cố production. Bước tiếp theo là khóa protocol đo, chạy đủ cặp Disabled/Active cùng nguồn lực, phân tích p95/p99, backlog/drain và cập nhật OpenAPI/runtime. API quản trị policy với audit vẫn cần làm trước khi bật cho khách thật.
