# Virtual Waiting Room trong TixFlow

Tài liệu này chuyển nội dung nghiên cứu trong DOCX sang dạng có thể tìm kiếm trong Git. Các ví dụ 5.000 người đến cùng lúc, 500 phiên active và admission thích ứng là kịch bản đề xuất, không phải tính năng hoặc số liệu benchmark đã xác nhận. Phần trạng thái hiện tại ở cuối tài liệu đối chiếu trực tiếp với mã và báo cáo local.

Bản Markdown chỉ chuyển phần chữ và bảng; hình cùng công thức nằm trong [bản DOCX](virtual-waiting-room-tixflow.docx). Kết quả kiểm chứng: [bước 9](virtual-waiting-room-phase-9.md), [đối chứng local](reports/waiting-room-step9-20261123.md), [soak 10 phút](reports/waiting-room-soak-20261123.md).

## 1. Virtual Waiting Room là gì?

Virtual Waiting Room là hệ thống phòng chờ trực tuyến, được sử dụng để kiểm soát lượng người dùng truy cập vào một website hoặc một chức năng có giới hạn khả năng xử lý.

Ý tưởng cốt lõi:

Người dùng truy cập website.

Hệ thống kiểm tra khả năng tiếp nhận.

Nếu còn chỗ, người dùng được phép truy cập.

Nếu đã đạt giới hạn, người dùng được đưa vào hàng đợi.

Hệ thống lần lượt cấp quyền truy cập cho người đang chờ.

Khi hết lượt truy cập hoặc hết thời gian cho phép, quyền truy cập được thu hồi và chuyển cho người tiếp theo.

## 2. Cách triển khai cơ chế hàng đợi

### 2.1. Quản lý thứ tự bằng FIFO

FIFO (First In, First Out) nghĩa là người vào trước được xử lý trước.

Ví dụ:

ZADD queue:event-01 1001 user-A

ZADD queue:event-01 1002 user-B

ZADD queue:event-01 1003 user-C

### 2.2. Cơ chế phân phối lượt truy cập

1. Đọc số người đang active.

2. Tính số slot còn trống.

3. Kiểm tra giới hạn tốc độ admission.

4. Lấy các ticket đầu hàng đợi.

5. Chuyển ticket sang ADMITTED.

6. Cấp admission token.

7. Gửi thông báo cho người dùng.

Công thức đơn giản: Xem công thức trong bản DOCX.

### 2.3. Ước tính thời gian chờ

Một tính năng hữu ích là cho người dùng biết họ phải chờ bao lâu.

Công thức cơ bản: Xem công thức trong bản DOCX.

Ví dụ:

Có 900 người phía trước.

Hệ thống cấp 10 người mỗi giây.

Thời gian chờ ước tính: Xem công thức trong bản DOCX.

## 3. Những cơ chế quan trọng cần xây dựng

| Cơ chế | Ý nghĩa | Trạng thái |
| --- | --- | --- |
| FIFO Queue | Đảm bảo thứ tự người dùng | Đã hoàn thành |
| Admission Control | Kiểm soát số lượng người được truy cập | Đang thực hiện |
| Rate Limiting | Giới hạn tốc độ cấp quyền | Đã hoàn thành |
| Session Management | Quản lý phiên truy cập | Đã hoàn thành |
| Ticket Expiration | Thu hồi ticket hết hạn | Đã hoàn thành |
| Reconnection | Cho phép người dùng kết nối lại | Đã hoàn thành |
| Anti-bot | Hạn chế bot chiếm dụng vị trí | Đang thực hiện |
| Access Token | Xác minh người dùng đã được cấp quyền | Đã hoàn thành |
| Monitoring | Theo dõi lưu lượng và hiệu suất | Đã hoàn thành |

## 4. Case Study

Kịch bản minh họa cần kiểm thử; các con số dưới đây là giả định, chưa phải phép đo của TixFlow.

Mô phỏng 5.000 người truy cập cùng một thời điểm vào một sự kiện.

Không có Virtual Waiting Room

**5.000**

Request đồng thời đổ vào ứng dụng

Có Virtual Waiting Room

**500**

Giới hạn active users giả định

Đây là cấu hình thử nghiệm giả định, không phải kết quả benchmark.

Các chỉ số cần thu thập:

| Chỉ số | Mục đích |
| --- | --- |
| Response Time | Thời gian phản hồi API |
| Throughput | Request xử lý mỗi giây |
| Error Rate | Tỷ lệ lỗi |
| CPU / RAM | Tài nguyên hệ thống |
| Queue Processing Time | Thời gian xử lý hàng đợi |
| Admission Accuracy | Có vượt quá capacity không |
| Queue Fairness | Có đảm bảo đúng thứ tự không |

Sử dụng k6 để mô phỏng lượng người dùng lớn và Grafana để trực quan hóa kết quả.

Đối chiếu với TixFlow: hệ thống đã điều tiết grant theo giây và bảo vệ Hold bằng admission token. Hệ thống chưa áp dụng giới hạn 500 phiên active; kết quả kiểm thử local được nêu ở phần cuối tài liệu.

| Cơ chế | Trạng thái | Triển khai | Cách triển khai trong code base |
| --- | --- | --- | --- |
| FIFO Queue | Đã hoàn thành | Redis Sorted Set, INCR, Lua Script | Mỗi lần join mới, Redis INCR cấp một sequence tăng dần. Entry được đưa vào Redis Sorted Set với sequence làm thứ tự; Lua Script lấy entry đầu hàng để cấp lượt. Thứ tự do server quyết định. |
| Admission Control | Đang thực hiện | Waiting Room Policy, BackgroundService, Admission Gate | WaitingRoomController chỉ cấp lượt khi policy là Active và suất diễn đang mở bán. Backend tạo Hold yêu cầu admission token khi phòng chờ được bật. Hiện cơ chế kiểm soát bằng tốc độ cấp lượt, chưa giới hạn tổng số người được truy cập đồng thời. |
| Rate Limiting | Đã hoàn thành | MaxGrantsPerSecond, Redis counter, fixed window rate limiter | Lua Script dùng Redis key đếm grant theo từng giây và chặn cấp thêm khi đạt MaxGrantsPerSecond; ngân sách này dùng chung giữa các API replica. Ngoài ra, API có fixed window rate limit theo IP, user và client cho request. |
| Session Management | Đã hoàn thành | Redis entry, queueId, subject, status | Phiên phòng chờ là một entry gắn với user và suất diễn, lưu trong Redis với queueId, trạng thái, thời điểm tham gia và hạn dùng. API hỗ trợ join, đọc status và rời hàng chờ; mỗi user chỉ có một entry còn hiệu lực cho mỗi suất diễn. Đây là quản lý phiên phòng chờ, chưa phải quản lý số phiên truy cập active đồng thời. |
| Ticket Expiration | Đã hoàn thành | TTL, expiresAt, trạng thái Expired | Với ticket/lượt của phòng chờ, entry đang chờ hết hạn sau 5 phút không hoạt động; quyền đã được cấp hết hạn sau 2 phút. Script đánh dấu Expired, loại entry chờ hết hạn khỏi queue và gate từ chối grant/token quá hạn. Việc dọn entry hết hạn diễn ra khi đọc hoặc xử lý queue, không phải một job thu hồi riêng. |
| Reconnection | Đã hoàn thành | sessionStorage, queueId, join idempotent | Frontend lưu queueId trong sessionStorage và dùng nó để lấy lại status sau khi tải lại trang hoặc gián đoạn. Nếu join lại bằng cùng user khi entry còn hạn, Redis trả entry cũ và giữ nguyên vị trí FIFO. |
| Anti-bot | Đang thực hiện | Authentication, one entry per user, API rate limit | Phòng chờ yêu cầu user đã xác thực với vai trò Customer; ánh xạ user–suất diễn ngăn một tài khoản tạo nhiều vị trí còn hiệu lực; API rate limit giảm việc gửi request liên tục. Chưa có CAPTCHA, challenge hoặc nhận diện nhiều tài khoản do cùng bot điều khiển. |
| Access Token | Đã hoàn thành | Admission token, ASP.NET Data Protection, Redis validation | Khi entry chuyển sang Admitted, API phát admission token được bảo vệ bằng ASP.NET Data Protection. Khi tạo Hold, backend kiểm tra token, user, suất diễn, thời hạn và grant tương ứng trong Redis; grant được ràng buộc với một Hold intent để chống dùng sai mục đích. |
| Monitoring | Đã hoàn thành | Prometheus metrics, Grafana dashboard, alert rules | API xuất metric HTTP và metric phòng chờ qua /internal/metrics; có Prometheus thu thập, Grafana dashboard và alert cho queue backlog, controller ngừng chạy hoặc gặp lỗi. Đây là phần mã và cấu hình đã xây dựng; repository chưa chứng minh monitoring production đang vận hành. |

| Đặc điểm | Ứng dụng |
| --- | --- |
| In-memory | Đọc ghi trạng thái nhanh |
| Sorted Set | Quản lý FIFO queue |
| Atomic commands | Cấp sequence và đếm ngân sách grant dùng chung |
| Lua Script | Xử lý admission atomic |
| TTL | Tự động hết hạn key |
| Pub/Sub | Một lựa chọn mở rộng; TixFlow hiện dùng polling |

## Các vấn đề kỹ thuật và phạm vi triển khai trong TixFlow

## 1. Concurrency – Truy cập đồng thời

Giả sử hệ thống có cấu hình:

Capacity: 500 người.

Active users hiện tại: 499 người.

Có 100 request cùng lúc muốn được admitted.

Một đoạn code đơn giản có thể như sau:

if (activeUsers < capacity)

{

activeUsers++;

return GenerateToken();

}

Nhưng hãy tưởng tượng 100 request cùng đọc activeUsers = 499 trước khi bất kỳ request nào cập nhật nó.

Kết quả: cả 100 request đều có thể được cấp quyền.

Minh họa race condition

Active Users = 499 / 500

Request 1

Đọc 499

Request 2

Đọc 499

Request 3

Đọc 499

Sai lệch

Nhiều request cùng được admitted
