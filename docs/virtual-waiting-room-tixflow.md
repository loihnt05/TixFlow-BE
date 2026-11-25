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
