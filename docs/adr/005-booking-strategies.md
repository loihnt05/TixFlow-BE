# ADR-005: Hai strategy booking dùng chung transaction

- Ngày: **07/10/2026**.
- Trạng thái: **Thiết kế đề xuất để triển khai**; quota/projection/hạn mức đã được người dùng xác nhận.
- Liên quan: [ADR-001](001-postgresql-source-of-truth.md), [business rules](../phase-1-business-rules.md), [ERD](../database.md).

## Bối cảnh

Vé General Admission chỉ cần số lượng; vé Reserved Seating phải giữ đúng ghế. Schema hiện hỗ trợ cả hai qua `TicketType.inventory_mode`, inventory, seats và allocation. Dùng cùng một counter cho cả hai sẽ không chứng minh được quyền sử dụng một ghế cụ thể.

## Quyết định

Booking module chọn strategy từ `TicketType.inventory_mode` ở DB. Client và AI không chọn một strategy để bỏ qua điều kiện. Các strategy cùng chạy trong transaction do command điều phối, cùng thứ tự khóa ADR-001 và cùng contract kết quả. Một Hold có thể chứa các TicketType cùng session; toàn bộ yêu cầu thành công hoặc rollback.

Quota thuộc từng TicketType, không thêm `EventSession.capacity`. Giới hạn `max_per_order` áp dụng riêng từng TicketType trong một Hold/Order; cộng tổng các dòng có cùng TicketType trước khi so sánh. Không cộng dồn giữa các Order khác nhau của cùng Customer. Default 10 trong schema không thay thế giá trị riêng của từng loại vé (seed VIP đang là 4). Kiểm tra giới hạn tại lúc tạo Hold; Organizer sửa giới hạn chỉ ảnh hưởng Hold mới, checkout giữ số lượng đã được chấp nhận trong Hold.

### GeneralAdmission

Nguồn quyết định là row `ticket_inventories`, với `total_quantity = ticket_types.capacity` và `held + sold <= total`. Tạo Hold dùng UPDATE có điều kiện số lượng và version; ghi HoldItem/counter trong cùng transaction. Ví dụ minh họa logic, chưa phải migration/SQL được triển khai:

```sql
UPDATE ticket_inventories
SET held_quantity = held_quantity + @quantity,
    version = version + 1,
    updated_at_utc = @decision_time
WHERE ticket_type_id = @ticket_type_id
  AND version = @expected_version
  AND @quantity > 0
  AND total_quantity - held_quantity - sold_quantity >= @quantity
RETURNING id, version;
```

Không đọc availability rồi UPDATE không điều kiện. Zero row yêu cầu phân biệt hết quota với version conflict để trả lỗi/retry phù hợp. Paid chuyển cùng lượng từ held sang sold; expiry/hủy trước thanh toán giảm held. Mọi điều chỉnh phải gắn với chuyển trạng thái Hold/Order hợp lệ một lần, không giảm counter chỉ vì nhận lại message.

### ReservedSeating

Nguồn quyết định là ghế active đúng session/TicketType cùng allocation. Kiểm tra toàn bộ seat ID khác nhau, số ghế bằng quantity; khóa các Seat theo thứ tự UUID; kiểm tra không còn allocation active và không có Ticket `Issued`/`Used` mâu thuẫn. Tạo allocation `Held` trỏ tới HoldItem và cùng deadline Hold.

Partial unique `ux_seat_allocations_active_seat` giới hạn một allocation `Held`/`Sold` cho mỗi ghế. `ux_tickets_active_seat` giới hạn một Ticket `Issued`/`Used` theo ghế. Partial unique index chỉ áp uniqueness trên các row thỏa predicate; việc giữ hai bảng cùng mô tả một ghế vẫn cần transaction của ứng dụng. [PostgreSQL 16 — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html).

Khi Sold, allocation đổi sang OrderItem và bỏ HoldItem/expiry theo constraint mục tiêu. Khi Released, allocation hết hiệu lực nhưng giữ tham chiếu lịch sử phù hợp. Không tự xóa một allocation Held chỉ vì đồng hồ đã qua expiry; command expiry phải kết thúc Hold/Order và giải phóng tất cả tài nguyên của chúng trong một transaction. Booking gặp allocation quá hạn có thể trả conflict tạm thời, yêu cầu worker giải phóng hoặc chạy expiry riêng trước khi thử lại.

Giữ một inventory row cho mỗi TicketType Reserved theo quyết định người dùng. `total_quantity` bằng capacity/số ghế active; `held_quantity` và `sold_quantity` là projection của allocation active. Strategy cập nhật projection trong cùng transaction, nhưng không dùng counter projection để quyết định ghế nào được cấp. Job đối soát khóa TicketType/Seat/Allocation/Inventory theo ADR-001 và dựng lại số đếm từ allocation; không ghi đè bằng snapshot đọc trước khi lấy khóa.

### Checkout, payment và expiry

Người dùng đã xác nhận ngày **07/10/2026**: checkout tạo một Order `PendingPayment` từ Hold và giữ Hold `Active`, allocation `Held`, counter `held`. Checkout không giữ tài nguyên lần thứ hai và không gia hạn deadline 10 phút. Payment thành công trước deadline chuyển Hold `Confirmed`, Order `Paid`, Payment `Succeeded`, allocation `Sold`, counter held sang sold và phát đúng số Ticket trong một transaction. Sale window chỉ áp cho Hold mới; Hold cũ giữ đủ TTL dù vượt giờ đóng bán/session bắt đầu. Xác nhận giảng viên vẫn **Decision Pending**, hạn **11/10/2026**.

Expiry chuyển Hold `Expired`, Order đang chờ `Expired`, kết thúc Payment đang chờ theo contract, giải phóng tài nguyên một lần. Callback mock đến trễ không hồi sinh Order hoặc lấy lại ghế từ Customer khác. Đây là mô phỏng quyết định thành công dưới gate DB; tích hợp cổng tiền thật sau này cần bổ sung reconciliation/refund khi tiền đã thu mà booking hết hạn.

Admin hủy Ticket chưa check-in chuyển Ticket `Cancelled`, allocation `Sold -> Released` và giảm sold; có thể bán lại nếu điều kiện bán vẫn hợp lệ. Hủy Ticket đã check-in giữ `checked_in_at_utc`, allocation `Sold` và sold quota, dù Ticket chuyển `Cancelled`; việc đã tham dự là dữ kiện không bị xóa. Hủy Ticket riêng giữ Order `Paid`; hủy cả Order chuyển Order `Cancelled` và hủy tất cả Ticket. Payment `Succeeded` không đổi thành `Refunded`, vì refund ngoài phạm vi. Hold `Confirmed` giữ lịch sử sau hủy Paid.

Hủy Event ghi parent `Cancelled` và outbox trước, trả `202`; worker cleanup từng giao dịch/vé theo các rule trên. Parent gate chặn booking/check-in ngay, không chờ cleanup. Nhãn session `SoldOut` chỉ là projection; không dùng giá trị có thể cũ này làm điều kiện từ chối độc lập khi quota đã được trả. Trạng thái terminal `Cancelled`/`Completed` và nguồn inventory/allocation mới là gate. Schema và cách cập nhật status cần thể hiện rõ khác biệt này khi triển khai.

## Phương án cân nhắc và hệ quả

| Phương án | Đánh giá |
|---|---|
| Tạo ghế giả cho mọi vé GA | Không chọn; tăng row/khóa mà không có định danh ghế thực cần bảo vệ. |
| Chỉ dùng inventory counter cho Reserved | Không chọn; counter không thể bảo đảm hai người không mua cùng ghế. |
| Bỏ inventory của Reserved | Không chọn theo xác nhận của người dùng; giữ projection để ít thay đổi schema. |
| Một transaction/loại vé rồi bù trừ khi thất bại | Không chọn trong một DB; all-or-nothing một transaction đơn giản và rõ hơn. |

GA có điểm tranh chấp ở inventory của TicketType; Reserved tranh chấp ở ghế nhưng vẫn cập nhật projection chung. Có thể tối ưu projection sau khi đo tải, nhưng không đổi nguồn quyết định ghế. Không hứa mức throughput cụ thể trước thử tải.

## Khoảng trống triển khai

- Viết hai strategy, command coordinator, allocation/quantity validation và expiry worker.
- Thêm version và siết `ck_seat_allocations_state`: Held có HoldItem, không OrderItem, có expiry; Sold có OrderItem, không HoldItem, không expiry.
- Bảo đảm session/type/customer của Hold, Order, Item, Seat và Ticket đồng nhất; constraint đơn cột hiện chưa bao trùm.
- Thêm `holds.currency` cho snapshot VND; triển khai sale window và TTL đã chốt, giữ cổng xác nhận giảng viên trong sổ quyết định.
- Unique seat ticket không áp dụng cho GA; thêm `tickets.item_ordinal` và unique `(order_item_id, item_ordinal)`, phát các ordinal từ 1 đến quantity dưới gate Order/Payment một lần. Kiểm tra phạm vi/số lượng trong command; unique ordinal riêng nó không giới hạn ordinal tối đa. Không dùng unique QR ngẫu nhiên để chứng minh không phát dư.

## Kịch bản nghiệm thu dự kiến

| ID | Tình huống | Kết quả phải đạt |
|---|---|---|
| ADR-005-TC-01 | Hai request cùng đặt vé GA cuối. | Tổng held + sold không vượt capacity; chỉ yêu cầu đủ quota thành công. |
| ADR-005-TC-02 | Hai Customer giữ cùng ghế. | Một allocation active; request thua rollback toàn Hold. |
| ADR-005-TC-03 | Hold gồm GA còn vé và Reserved có ghế đã chiếm. | Cả yêu cầu rollback, không giữ riêng phần GA. |
| ADR-005-TC-04 | Gửi hai dòng cùng TicketType, mỗi dòng dưới max nhưng tổng vượt max. | Từ chối theo tổng; hai Order độc lập vẫn xét riêng. |
| ADR-005-TC-05 | Callback thành công lặp bằng event ID cũ hoặc event ID mới. | Không tăng sold/phát Ticket lần hai, kể cả GA không có seat ID. |
| ADR-005-TC-06 | Expiry/hủy được gọi lặp. | Held giảm đúng một lần; counter không âm. |
| ADR-005-TC-07 | Projection Reserved bị lệch. | Không cấp ghế dựa vào projection; đối soát khôi phục số đếm đúng mà không đè cập nhật mới. |
| ADR-005-TC-08 | Giảm quota dưới held + sold hoặc tắt ghế active allocation. | Từ chối; thay đổi cấu hình không làm mất quyền giữ/mua hiện có. |
| ADR-005-TC-09 | Checkout retry hoặc đổi sang idempotency key mới với cùng Hold. | Unique Hold–Order giữ một Order; held không tăng và deadline không đổi. |
| ADR-005-TC-10 | Callback mock tới sau khi tài nguyên đã cấp cho Hold khác. | Không khôi phục Order cũ; không ảnh hưởng allocation mới. |
| ADR-005-TC-11 | Admin hủy một vé Issued và một vé đã Used. | Vé chưa dùng trả sold; vé đã dùng giữ sold/allocation và check-in timestamp; Payment giữ Succeeded. |
| ADR-005-TC-12 | Sale window đóng hoặc max_per_order giảm sau khi đã tạo Hold. | Checkout/payment của Hold còn hiệu lực giữ giá/quota/deadline đã chấp nhận; tạo Hold mới kiểm tra cấu hình mới. |

