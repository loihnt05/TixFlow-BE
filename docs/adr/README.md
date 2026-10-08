# ADR Giai đoạn 1

- Ngày lập: **07/10/2026**.
- Phạm vi: quyết định kiến trúc phục vụ khóa yêu cầu và thiết kế; không xác nhận rằng runtime đã triển khai đầy đủ.
- Trạng thái chung: **Thiết kế đề xuất để triển khai**. PostgreSQL, Redis, RabbitMQ và Keycloak kế thừa [kiến trúc nền](../architecture.md); chi tiết giao dịch, phục hồi và kiểm tra quyền dưới đây là thiết kế mục tiêu.

| ADR | Quyết định | Hiện trạng |
|---|---|---|
| [ADR-001](001-postgresql-source-of-truth.md) | PostgreSQL quyết định trạng thái nghiệp vụ, transaction và thứ tự khóa thống nhất. | Có schema/EF Core; command giao dịch chưa có. |
| [ADR-002](002-redis-rebuildable-data.md) | Redis lưu cache/rate limit/dữ liệu tạm có thể dựng lại; expiry nghiệp vụ dựa trên PostgreSQL. | Có container/cấu hình; cache/rate limit chưa triển khai. |
| [ADR-003](003-rabbitmq-transactional-outbox.md) | RabbitMQ nhận sự kiện qua transactional outbox; consumer xử lý idempotent. | Có bảng outbox; publisher, consumer/inbox và retry worker chưa triển khai. |
| [ADR-004](004-keycloak-identity-and-authorization.md) | Keycloak quản lý xác thực; API kiểm tra một role và ownership bằng hồ sơ local. | Đã siết claim/single-role ngày 08/10/2026; quyền trên từng object còn theo module. |
| [ADR-005](005-booking-strategies.md) | GeneralAdmission dùng counter có điều kiện; ReservedSeating dùng ghế/allocation và unique index. | Có cấu trúc dữ liệu/constraint nền; booking command chưa triển khai. |

## Quy tắc đọc và thay đổi

Mỗi ADR ghi bối cảnh, quyết định, phương án đã cân nhắc, hệ quả, khoảng trống triển khai và kịch bản nghiệm thu dự kiến. Mã `ADR-00x-TC-yy` là đầu vào cho giai đoạn viết test; không phải bằng chứng test đã chạy.

Quyết định sản phẩm được ghi tại [quy tắc nghiệp vụ](../phase-1-business-rules.md) và [actor/use case](../phase-1-actors-and-use-cases.md). Người dùng đã xác nhận ngày **07/10/2026**: VND và snapshot giá tại Hold; cửa sổ mở bán là giao Event/TicketType cho Hold mới; Hold 10 phút, checkout giữ deadline cũ kể cả qua giờ đóng bán/bắt đầu session; hủy trước thanh toán bởi Customer, hủy vé đã trả tiền bởi Admin; refund ngoài phạm vi; mock payment cho Customer chọn success/failure và retry trong TTL; check-in từ 60 phút trước session đến trước lúc kết thúc. Hủy Ticket chưa dùng trả quota, Ticket đã check-in vẫn chiếm sold quota; hủy Ticket riêng giữ Order Paid, hủy cả Order chuyển Cancelled, Payment Succeeded giữ lịch sử.

Vòng đời thống nhất: checkout giữ Hold `Active`; Order `PendingPayment` dùng deadline của Hold; thanh toán thành công chuyển cả bộ dữ liệu trong một transaction; hết hạn giải phóng một lần. Hủy Event chặn thao tác mới ngay rồi dùng worker dọn các giao dịch/vé liên quan. Chi tiết tại [state diagrams](../phase-1-state-diagrams.md).

Chưa có bằng chứng giảng viên phê duyệt bộ thiết kế, vì vậy cổng xác nhận giảng viên vẫn **Decision Pending**, hạn **11/10/2026**. Xác nhận của người dùng không được ghi thành xác nhận của giảng viên; trạng thái này không biến các lựa chọn sản phẩm đã trả lời thành câu hỏi chưa trả lời.

Khi thay đổi một quyết định đã chấp thuận, tạo ADR thay thế và ghi liên kết `Superseded by`; không sửa lịch sử để khiến quyết định cũ trông như chưa từng tồn tại.

## Tài liệu liên quan

- [ERD, khóa, index, version và UTC](../database.md).
- [State diagrams](../phase-1-state-diagrams.md).
- [Sequence diagrams](../phase-1-sequence-diagrams.md).
- [API contract](../phase-1-api-contract.md).

