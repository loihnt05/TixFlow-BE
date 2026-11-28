# Virtual Waiting Room trong TixFlow

Tài liệu này đối chiếu ý tưởng trong [bản Word](virtual-waiting-room-tixflow.docx) với phần đã triển khai trong backend. TixFlow dùng hàng chờ theo suất diễn để điều tiết **tốc độ được thử tạo Hold**. Quyền vào hàng chờ không giữ sẵn vé; PostgreSQL vẫn quyết định tồn kho và kết quả đặt chỗ. Kết quả đo hiện có chỉ xác nhận môi trường local với workload đã công bố.

## Phạm vi hiện tại

Mỗi `eventSessionId` có một policy `Disabled`, `Active`, `Paused` hoặc `Closed`, mặc định `Disabled`. Khi `Active`, các API replica cùng đọc queue và cấp grant theo `MaxGrantsPerSecond`. `Paused` dừng cấp lượt nhưng vẫn giữ gate tạo Hold. `Closed` dừng nhận khách vào hàng. Khi policy không bật, khách đi thẳng tới luồng Hold thông thường.

Gate áp dụng tại `POST /api/v1/holds` và lời gọi tạo Hold từ Assistant. Nó không bảo vệ việc xem catalog, checkout hoặc thanh toán cho một Hold đã tồn tại. Grant chỉ cho phép **thử** tạo Hold; khách vẫn có thể nhận lỗi hết vé.

Mã tham chiếu: [policy](../src/TixFlow.Domain/WaitingRoom/WaitingRoomPolicy.cs), [API phòng chờ](../src/TixFlow.Api/Modules/WaitingRoom/WaitingRoomModule.cs), [gate tạo Hold](../src/TixFlow.Api/Modules/Booking/BookingModule.cs).

## FIFO và trạng thái dùng chung

Redis cấp sequence bằng `INCR` phía server. Entry được xếp trong Sorted Set theo sequence; Lua script lấy phần tử đầu còn hiệu lực và chuyển sang `Admitted`. Cùng một người dùng tham gia lại một suất diễn khi entry còn hiệu lực sẽ nhận lại entry cũ và giữ vị trí. Thời gian do trình duyệt gửi không quyết định thứ tự.

Các API replica dùng cùng Redis và cùng ngân sách grant theo giây. Mỗi replica có controller, nhưng Lua script kiểm tra ngân sách chung trước khi cấp lượt, nên nhiều controller không được tự nhân hạn mức. Redis không phải nguồn sự thật về số vé.

Mã tham chiếu: [RedisWaitingRoomStore](../src/TixFlow.Infrastructure/WaitingRoom/RedisWaitingRoomStore.cs), [WaitingRoomController](../src/TixFlow.Api/Modules/WaitingRoom/WaitingRoomController.cs). Kiểm thử đa replica và restart: [WaitingRoomResilienceTests](../tests/TixFlow.Api.Tests/WaitingRoomResilienceTests.cs).

## Vòng đời entry và grant

Các trạng thái có trong mã là `Waiting`, `Admitted`, `Consumed`, `Expired`, `Abandoned` và `Rejected`. Entry đang chờ hết hạn sau năm phút không hoạt động; gọi status hoặc join lại sẽ gia hạn thời gian chờ. Grant đã cấp hết hạn sau hai phút. Việc hủy hàng chờ đánh dấu `Abandoned`; khi suất diễn không còn mở bán, entry có thể bị đánh dấu `Rejected`.

`Consumed` nghĩa là grant đã được ràng buộc với một Hold intent, không chứng minh Hold thành công. Khi retry cùng idempotency key và cùng request hash, gate cho phép phát lại intent đó. Dùng grant cho intent khác sẽ bị từ chối. Token được bảo vệ bằng ASP.NET Data Protection và gắn với subject, suất diễn, queue ID, grant ID và hạn dùng. Backend kiểm tra cả token lẫn trạng thái grant trong Redis.

Mã đang bổ sung giới hạn số admission grant chưa dùng còn hạn bằng Redis Sorted Set và `maxConcurrentAdmissions` theo suất diễn; test tích hợp chưa chạy trên máy hiện tại. TixFlow **chưa** có trạng thái phiên truy cập `ACTIVE`/`COMPLETED`, heartbeat cho người đã admitted hoặc cơ chế giữ slot xuyên suốt checkout. Vì vậy giới hạn grant này không chứng minh số người đang mua vé đồng thời.

Mã tham chiếu: [AdmissionTokens](../src/TixFlow.Api/Modules/WaitingRoom/AdmissionTokens.cs), [RedisWaitingRoomStore](../src/TixFlow.Infrastructure/WaitingRoom/RedisWaitingRoomStore.cs), [BookingModule](../src/TixFlow.Api/Modules/Booking/BookingModule.cs).

## Điều tiết tải và ví dụ 5.000 người

Ví dụ 5.000 người đến cùng lúc và chỉ cho 500 người active trong bản Word là **mô hình nghiên cứu**. Bản hiện tại giới hạn số grant trong mỗi giây bằng `MaxGrantsPerSecond`; phần mã mới giới hạn thêm grant chưa dùng nhưng chưa có bất biến `activeUsers <= capacity` cho toàn hành trình mua vé. API cũng có rate limit theo IP, user và client để giảm lạm dụng request. Chưa có thuật toán tự điều chỉnh grant rate dựa trên latency, CPU, memory hoặc lỗi.

Muốn triển khai giới hạn active thật, cần định nghĩa rõ thời điểm chiếm và giải phóng slot, timeout, retry, nhiều tab, nhiều replica và xử lý khi Redis mất kết nối. Phép thử cần chứng minh bất biến `activeUsers <= capacity` dưới tranh chấp đồng thời, thay vì chỉ quan sát một biểu đồ thông lượng.

## Cập nhật trạng thái cho client

TixFlow chọn polling. API trả `pollAfterMs` ổn định theo `queueId` trong khoảng 3–5 giây; frontend và k6 thêm jitter 0–500 ms để phân tán các lần hỏi. Status trả vị trí **ước lượng**, trạng thái entry và admission token khi đến lượt. Client lưu `queueId` để có thể đọc lại status sau khi refresh. Hệ thống hiện không dùng SignalR, WebSocket hoặc Redis Pub/Sub cho đường cập nhật này.

Mã tham chiếu: [WaitingRoomModule](../src/TixFlow.Api/Modules/WaitingRoom/WaitingRoomModule.cs). Phép đo sau thay đổi polling: [báo cáo bước 9](reports/waiting-room-step9-20261123.md).

## Bảo vệ đường tạo Hold

Khi policy đang bảo vệ suất diễn, client gọi trực tiếp API tạo Hold cũng phải trình admission token. Gate kiểm tra người dùng, suất diễn, thời hạn, grant và Hold intent; việc bảo vệ giao diện frontend một mình không đủ. Khi Redis không thể xác minh grant, gate trả `503 WAITING_ROOM_UNAVAILABLE` và `Retry-After` thay vì bỏ qua phòng chờ.

Một tài khoản chỉ có một entry còn hiệu lực cho mỗi suất diễn và các endpoint yêu cầu xác thực Customer. Đây là các lớp giảm lạm dụng hiện có; dự án chưa chứng minh CAPTCHA, phát hiện bot nhiều tài khoản hoặc chống DDoS ở tầng mạng.

Mã tham chiếu: [WaitingRoomModule](../src/TixFlow.Api/Modules/WaitingRoom/WaitingRoomModule.cs), [BookingModule](../src/TixFlow.Api/Modules/Booking/BookingModule.cs).

## Độ tin cậy và vận hành

Các bài test đã kiểm tra hai API host dùng chung trạng thái, controller thay thế và Redis restart trong điều kiện thử nghiệm. API quản trị policy ghi audit, từ chối chuyển sang `Active` khi Redis không sẵn sàng, nhưng vẫn cho phép `Paused` hoặc `Closed` để xử lý sự cố. Runbook mô tả cách quan sát queue depth, controller và chuyển trạng thái policy.

Điều kiện còn thiếu trước khi bật cho khách thật là diễn tập failover Redis và lỗi mạng trên môi trường triển khai, theo dõi alert thực tế, kiểm tra rollback và đo tải trên tài nguyên tương đương production. Kết quả local không phải SLA production.

Tham chiếu: [runbook](virtual-waiting-room-operations.md), [bước 8](virtual-waiting-room-phase-8.md), [bước 9](virtual-waiting-room-phase-9.md).

## Bằng chứng hiệu năng hiện có

Ba cặp run Active/Disabled của bước 9 dùng cùng image, dataset và tài nguyên. Mỗi run có 100 customer bắt đầu join trong một giây, 50 Hold/Paid/Ticket và 50 conflict hết vé. Cả ba run Active đạt status p95 lần lượt 246, 187 và 56 ms; không có lỗi kỹ thuật, iteration drop, oversell hoặc thiếu vé sau thanh toán. Đây là phép so sánh local, không phải thử nghiệm 5.000 người đồng thời.

Soak local mười phút phân bổ 1.000 lượt join trên 600 giây (khoảng 1,67 lượt/giây), đạt 1.000 grant, 50 Hold/Paid/Ticket và 950 conflict hết vé. Soak xác minh ổn định ở tốc độ đến thấp; không thay thế flash crowd hoặc failover.

Số liệu, cấu hình và giới hạn phép đo: [đối chứng bước 9](reports/waiting-room-step9-20261123.md), [soak 10 phút](reports/waiting-room-soak-20261123.md).

## Việc tiếp theo có thể kiểm chứng

1. Định nghĩa và triển khai capacity active nếu đề tài cần chứng minh `activeUsers <= capacity`, kèm lifecycle và test tranh chấp đa replica.
2. Đo workload 5.000 join theo protocol cố định; báo riêng số grant, Hold thành công, conflict hết vé, lỗi kỹ thuật, p95 và thời gian drain.
3. Diễn tập Redis mất kết nối, restart đột ngột và failover nhiều máy; chứng minh gate fail closed và quy trình khôi phục.
4. Chỉ nghiên cứu admission thích ứng sau khi có tín hiệu tải đáng tin cậy, ngưỡng và cơ chế chống dao động; so sánh với policy grant rate cố định trên cùng workload.
