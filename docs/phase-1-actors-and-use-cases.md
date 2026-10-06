# Đặc tả actor và use case

**Giai đoạn:** 1 - Khóa yêu cầu và thiết kế  
**Phạm vi:** Hai đầu việc đầu: quyền Customer, Organizer, Admin và use case nghiệp vụ  
**Trạng thái:** Bản dự thảo để chốt; các mục `Decision Pending` cần xác nhận trước 11/10/2026.

Tài liệu này chuyển phạm vi kế hoạch TixFlow thành một mô hình quyền và các luồng nghiệp vụ có thể kiểm tra. Đây là đặc tả thiết kế, không khẳng định rằng các luồng đã được cài đặt: ở baseline hiện tại phần lớn API catalog, booking, order, payment, ticket và AI vẫn chưa hoàn chỉnh.

## 1. Thuật ngữ và nguyên tắc danh tính

| Thuật ngữ | Ý nghĩa trong đặc tả |
|---|---|
| `Customer` | Người mua vé; chỉ được truy cập hold, order, payment và ticket do chính tài khoản của mình tạo hoặc sở hữu. |
| `Organizer` | Tài khoản người tổ chức gắn với một hồ sơ `Organizer`; chỉ quản lý tài nguyên thuộc hồ sơ đó. |
| `Admin` | Tài khoản vận hành toàn hệ thống; thao tác hỗ trợ hoặc ghi đè trạng thái phải có audit. |
| `Event` | Sự kiện do một `Organizer` sở hữu. |
| `EventSession` | Một suất diễn của `Event`; các loại vé, ghế và booking gắn với suất diễn. |
| `Hold` | Lượt giữ tạm thời vé hoặc ghế, có thời điểm hết hạn. |
| `Order` | Đơn hàng được tạo từ một `Hold`. |
| `Payment` | Giao dịch thanh toán của một `Order`; trong phạm vi khóa luận là giả lập. |
| `Ticket` | Vé được phát hành sau khi thanh toán thành công; có mã vé và QR. |

- API xác thực tài khoản qua Keycloak. `sub` trong access token là định danh ngoài; API ánh xạ nó sang `users.id` cục bộ trước khi kiểm tra quyền sở hữu.
- Role được lấy từ claim `roles` đã xác thực. Không lấy role, `userId`, `ownerUserId` hay `organizerId` do client gửi lên làm căn cứ phân quyền.
- Catalog công khai chỉ hiển thị sự kiện `Published`. Availability đọc được là ảnh chụp tại thời điểm truy vấn; thao tác tạo `Hold` luôn kiểm tra lại trong transaction.
- API kiểm tra cả role lẫn quyền trên đúng object. Kiểm tra ẩn/hiện nút trên frontend chỉ phục vụ giao diện, không thay authorization tại backend.

## 2. Actor và ownership

### 2.1 Customer

Customer có thể duyệt catalog công khai không cần đăng nhập. Các thao tác tạo hold, checkout, thanh toán giả lập, xem order/ticket và yêu cầu hủy cần tài khoản đang hoạt động. Quyền trên tài nguyên cá nhân luôn được giới hạn theo `user_id` của danh tính đã xác thực. Mỗi tài khoản chỉ mang một role nghiệp vụ; tài khoản Organizer hoặc Admin không đồng thời thực hiện quyền mua vé của Customer.

### 2.2 Organizer

Ownership của Organizer được xác định theo chuỗi quan hệ dữ liệu:

```text
authenticated sub -> users.id -> organizers.owner_user_id
                                  organizers.id -> events.organizer_id
                                                   -> EventSession và tài nguyên con
```

- Organizer được đọc/sửa hồ sơ tổ chức của mình và quản lý Event, EventSession, TicketType, seat map, inventory cùng dữ liệu check-in thuộc Event của mình.
- Quyền với tài nguyên con được suy ra từ `Event.organizer_id`; không cần và không được tin vào một `organizerId` do client tự chọn.
- Organizer không được sửa hoặc hủy Event của một Organizer khác. Việc biết hoặc đoán được UUID của tài nguyên không làm phát sinh quyền truy cập.
- Organizer chỉ xem số liệu bán vé tổng hợp và thông tin tối thiểu cần để check-in. Không trả email khách hàng, mã QR thô hoặc thông tin thanh toán cá nhân trong màn hình quản lý thông thường.
- Một hồ sơ Organizer có đúng một owner; owner có thể tạo và quản lý nhiều Event cùng lúc. Số Event không bị giới hạn một Event trên mỗi Organizer.
- Schema hiện có `organizers.owner_user_id` unique, phù hợp với một owner duy nhất cho mỗi hồ sơ và tối đa một hồ sơ Organizer trên mỗi user. Không cần hỗ trợ cộng tác viên/nhân viên thuộc cùng tổ chức trong phạm vi hiện tại.

### 2.3 Admin

Admin được quản trị mọi hồ sơ Organizer, Event và giao dịch khi cần hỗ trợ hoặc xử lý vận hành. Admin được hủy Hold, Order, Ticket và thực hiện check-in cho Event thuộc bất kỳ Organizer nào. Các thao tác này phải ghi actor, object, thời điểm và lý do vào audit log. Quyền hủy Order/Ticket không tự quyết định việc hoàn tiền; chính sách hoàn tiền vẫn cần chốt riêng. Admin không được tự đánh dấu một khoản Payment là thành công để bỏ qua luồng thanh toán.

### 2.4 Ma trận quyền đề xuất

`✓` cho phép; `✓ (own)` chỉ trên tài nguyên sở hữu; `read-min` chỉ đọc phần dữ liệu tối thiểu cần cho nghiệp vụ; `-` không có quyền theo role đó.

| Năng lực | Khách chưa đăng nhập | Customer | Organizer | Admin |
|---|---:|---:|---:|---:|
| Duyệt catalog đã publish | ✓ | ✓ | ✓ | ✓ |
| Xem draft/private Event | - | - | ✓ (own) | ✓ |
| Tạo/sửa hồ sơ Organizer và catalog | - | - | ✓ (own) | ✓ |
| Tạo, xem, hủy Hold | - | ✓ (own) | - | Đọc/hủy toàn hệ thống; audit |
| Tạo Order từ Hold | - | ✓ (own) | - | - |
| Thực hiện thanh toán giả lập | - | ✓ (own order) | - | - |
| Xem Order và Payment | - | ✓ (own) | read-min, tổng hợp theo Event | ✓; audit khi thao tác |
| Xem hoặc tải Ticket/QR của khách | - | ✓ (own) | - | ✓ khi hỗ trợ; audit |
| Kiểm tra trạng thái Ticket và check-in | - | - | ✓ (own Event) | ✓ (toàn hệ thống); audit |
| Hủy Hold | - | ✓ (own, còn hiệu lực) | - | ✓ toàn hệ thống; audit |
| Hủy Order/Ticket hoặc xử lý hoàn tiền | - | ✓ theo chính sách chờ chốt | - | ✓ toàn hệ thống; refund theo chính sách chờ chốt; audit |
| Hủy Event | - | - | ✓ (own) | ✓ |
| Dùng AI Assistant để tìm và đề xuất | - | ✓ | Chỉ đọc catalog công khai | Chỉ đọc catalog công khai |
| Yêu cầu AI tạo Hold | - | ✓, sau xác nhận rõ ràng | - | - |

**Ranh giới quyền đã chốt:** mỗi tài khoản có đúng một role nghiệp vụ. Organizer và Admin không thực hiện quyền mua vé của Customer; muốn mua vé cần dùng tài khoản Customer riêng.

## 3. Quy tắc dùng chung cho use case

1. Với thao tác trên dữ liệu riêng tư, backend ràng buộc truy vấn bằng chủ thể đã xác thực hoặc ownership của Event, rồi mới trả dữ liệu hay thay đổi trạng thái.
2. Hold và Order gắn với đúng một Customer. Order chỉ được tạo từ Hold còn hiệu lực và mỗi Hold tạo tối đa một Order.
3. Hold chứa giá snapshot tại thời điểm giữ; checkout không âm thầm đổi giá đã hiển thị trong Hold.
4. Request có thể retry phải nhận kết quả idempotent khi cùng key và cùng payload. Dùng lại key với payload khác phải bị từ chối; chi tiết HTTP/status thuộc đầu việc API contract sau.
5. Hold nhiều vé/ghế là all-or-nothing: nếu một phần không khả dụng thì không để lại một phần tài nguyên đã giữ.
6. Ticket chỉ phát hành sau khi Payment thành công. QR thô chỉ được đưa cho người sở hữu vé lúc phát hành/hiển thị; database lưu hash như schema hiện có.
7. Không thao tác qua AI được coi là hoàn tất cho đến khi API nghiệp vụ xác nhận. Availability AI đọc được không bảo đảm Hold thành công.

## 4. Đặc tả use case

### UC-01 - Duyệt catalog sự kiện

**Actor chính:** Khách chưa đăng nhập hoặc Customer.  
**Mục tiêu:** Tìm Event đã công bố, xem thông tin, suất diễn, loại vé và availability hiện tại.

**Điều kiện trước:** Không cần đăng nhập. Event phải ở trạng thái `Published`; suất diễn công khai phải ở trạng thái bán/được phép hiển thị theo quy tắc catalog.

**Luồng chính:**

1. Actor mở danh sách hoặc tìm theo tên, thể loại, địa điểm, ngày.
2. API lọc Event công khai, trả trang kết quả và thông tin tóm tắt.
3. Actor mở một Event và chọn EventSession.
4. API trả chi tiết, TicketType/seat map phù hợp và availability snapshot nếu có.
5. Actor chọn loại vé/ghế để bắt đầu Hold; kết quả availability được xem là tham khảo cho tới khi Hold thành công.

**Ngoại lệ:** Event chưa publish hoặc đã bị gỡ khỏi catalog không lộ qua danh sách/tìm kiếm/chi tiết công khai. Nếu availability cũ, API Hold sẽ xác nhận lại và có thể từ chối.

**Kịch bản nghiệm thu dự kiến:** draft không xuất hiện với khách; Event published xuất hiện; availability giảm trước khi tạo Hold không dẫn tới trả lời “đã giữ” nếu API Hold thất bại.

### UC-02 - Quản lý catalog thuộc Organizer

**Actor chính:** Organizer; Admin là actor hỗ trợ/quản trị toàn cục.  
**Mục tiêu:** Tạo và cập nhật hồ sơ tổ chức, Event, EventSession, TicketType, seat map và inventory do Organizer sở hữu.

**Điều kiện trước:** Organizer đã đăng nhập, tài khoản hoạt động và ánh xạ được tới một hồ sơ Organizer. Admin phải được xác thực với role tương ứng.

**Luồng chính:**

1. Organizer yêu cầu danh sách tài nguyên của mình.
2. API tự xác định `organizerId` từ `owner_user_id`, không lấy owner từ body/query.
3. Organizer tạo hoặc sửa Event và các tài nguyên con.
4. Trước khi công bố, API kiểm tra quyền sở hữu và các điều kiện dữ liệu cần thiết.
5. Khi Event được công bố, Event trở nên đọc được trong catalog công khai theo UC-01.

**Ngoại lệ:** Organizer gọi bằng ID của tổ chức/Event khác thì request không đọc hoặc sửa được tài nguyên đó. Admin có thể quản lý mọi tổ chức; thay đổi bởi Admin phải được audit.

**Kịch bản nghiệm thu dự kiến:** Organizer A không thể đọc, sửa hoặc publish Event của Organizer B kể cả khi biết UUID; Organizer A chỉ thấy tài nguyên của mình; Admin có thể truy cập hai phía và thao tác có dấu vết audit.

### UC-03 - Tạo hoặc giải phóng Hold

**Actor chính:** Customer.  
**Mục tiêu:** Tạm giữ một hay nhiều vé/ghế để checkout trong thời hạn.

**Điều kiện trước:** Customer đã đăng nhập bằng tài khoản hoạt động; EventSession và TicketType đang cho phép bán; request nêu rõ số lượng hoặc danh sách seat ID và có idempotency key.

**Luồng chính:**

1. Customer chọn vé hoặc ghế và gửi yêu cầu Hold.
2. API kiểm tra sale window, trạng thái EventSession/TicketType, thông tin request và các giới hạn áp dụng cho Customer đã được chốt.
3. PostgreSQL xác nhận sức chứa/ghế còn khả dụng và tạo Hold cùng HoldItem/SeatAllocation trong một transaction.
4. API trả Hold ID, mặt hàng, giá snapshot, thời hạn UTC và trạng thái.
5. Customer có thể chủ động hủy Hold còn `Active`; tài nguyên được giải phóng đúng một lần.

**Ngoại lệ:** Nếu một ghế hoặc một phần số lượng không thể cấp, toàn bộ Hold thất bại và không giữ phần còn lại. Retry cùng key/payload trả cùng kết quả; cùng key nhưng payload khác bị từ chối. Hold đã hết hạn/hủy/xác nhận không được giải phóng lần nữa.

**Kịch bản nghiệm thu dự kiến:** hai Customer tranh cùng ghế chỉ một người giữ thành công; số lượng held không vượt capacity; retry không tạo Hold thứ hai; hủy Hold hai lần chỉ giải phóng một lần.

### UC-04 - Checkout Hold thành Order

**Actor chính:** Customer.  
**Mục tiêu:** Chuyển Hold còn hiệu lực thành một Order đang chờ thanh toán.

**Điều kiện trước:** Customer sở hữu Hold đang `Active`, chưa hết hạn và chưa được chuyển thành Order.

**Luồng chính:**

1. Customer yêu cầu checkout cho Hold.
2. API kiểm tra lại chủ sở hữu, thời hạn và trạng thái Hold.
3. API tạo Order/OrderItem từ giá và số lượng snapshot trong Hold.
4. Hold được xác nhận chuyển trạng thái theo cùng ranh giới giao dịch; Order ở `PendingPayment`.
5. API trả mã Order, tổng tiền, tiền tệ và trạng thái.

**Ngoại lệ:** Hold không còn hiệu lực thì checkout bị từ chối và không tạo Order. Lặp checkout không tạo Order thứ hai; Hold đã có Order trả lại cùng Order theo quy tắc idempotency.

**Kịch bản nghiệm thu dự kiến:** một Hold có tối đa một Order; OrderItem khớp HoldItem; retry checkout không nhân đôi Order hoặc giảm inventory lần nữa.

### UC-05 - Thanh toán giả lập

**Actor chính:** Customer.  
**Mục tiêu:** Hoàn tất hoặc thất bại thanh toán mô phỏng cho Order của Customer.

**Điều kiện trước:** Customer sở hữu Order `PendingPayment`; số tiền và tiền tệ lấy từ Order, không tin số tiền client gửi.

**Luồng chính:**

1. Customer khởi tạo Payment giả lập bằng key chống lặp.
2. API tạo/tra Payment `Pending` gắn với Order và chạy kết quả mô phỏng theo cấu hình môi trường phát triển.
3. Kết quả thành công được áp dụng tối đa một lần: Payment thành `Succeeded`, Order thành `Paid`, sau đó phát hành Ticket.
4. Kết quả thất bại không phát hành Ticket và trả trạng thái Payment/Order có thể xử lý tiếp theo quy tắc đã chốt.
5. API trả kết quả; callback giả lập gửi lặp được nhận diện bằng provider event ID và không áp dụng lại.

**Ngoại lệ:** Payment cho Order người khác bị từ chối. Sai số tiền/tiền tệ không làm thay đổi Order. Callback lặp không tạo Payment hoặc Ticket trùng.

**Kịch bản nghiệm thu dự kiến:** cùng key tạo tối đa một Payment; callback lặp chỉ ghi nhận kết quả một lần; Payment thất bại không cấp Ticket; tổng thu không bị cộng hai lần.

### UC-06 - Tra cứu và sử dụng Ticket

**Actor chính:** Customer; Organizer/Admin chỉ truy cập phần phục vụ kiểm tra/check-in.  
**Mục tiêu:** Customer xem vé đã phát hành và lấy QR cần thiết để tham dự.

**Điều kiện trước:** Ticket gắn với Order đã thanh toán thành công, trừ khi trạng thái Ticket đã bị hủy/hoàn theo chính sách.

**Luồng chính:**

1. Customer mở danh sách Ticket của mình.
2. API lọc theo `tickets.user_id` từ danh tính đã xác thực.
3. Customer xem thông tin EventSession, mã vé, ghế (nếu có), trạng thái và QR dùng cho check-in.
4. API chỉ phát QR cho vé hợp lệ của đúng Customer; QR token thô không được đưa vào log hoặc trả trong truy vấn của Organizer.

**Ngoại lệ:** Customer đoán ID Ticket của người khác không xem/tải được. Vé `Cancelled`, `Refunded` hoặc `Used` không được trình bày như vé còn dùng được.

**Kịch bản nghiệm thu dự kiến:** Customer A không xem được Ticket của B; Payment chưa thành công không có Ticket; QR thô không xuất hiện trong log hoặc response của Organizer.

### UC-07 - Check-in Ticket

**Actor chính:** Organizer sở hữu Event; Admin được check-in trên toàn hệ thống.  
**Mục tiêu:** Xác minh Ticket tại cổng và đánh dấu đã sử dụng đúng một lần.

**Điều kiện trước:** Ticket tồn tại, chưa bị hủy/hoàn/đã dùng; EventSession thuộc Organizer đang xác thực. QR trình ra phải kiểm tra được bằng hash lưu trong hệ thống.

**Luồng chính:**

1. Nhân viên Organizer quét QR hoặc nhập mã vé.
2. API xác thực Ticket, session, trạng thái và quyền sở hữu Event của Organizer.
3. API thực hiện chuyển trạng thái `Issued` → `Used` một cách nguyên tử, ghi `checked_in_at_utc` và actor.
4. API trả kết quả check-in và thông tin tối thiểu để xác nhận người tham dự.

**Ngoại lệ:** QR không hợp lệ, vé thuộc Event khác, ticket đã hủy/hoàn hoặc Organizer không sở hữu Event thì check-in thất bại. Quét lặp không tạo lần check-in thứ hai và trả kết quả “đã sử dụng” theo quy tắc API.

**Kịch bản nghiệm thu dự kiến:** hai lần quét đồng thời chỉ một lần đổi trạng thái; Organizer khác không thể check-in Ticket; Admin thao tác được nhưng có audit.

### UC-08 - Hủy Hold, Order, Ticket hoặc Event

**Actor chính:** Customer đối với tài nguyên của mình; Organizer đối với Event của mình; Admin toàn hệ thống.  
**Mục tiêu:** Hủy đúng đối tượng, áp dụng một lần và giữ nhất quán giữa tài nguyên vé, order và payment.

**Luồng chính:**

1. Customer yêu cầu hủy Hold còn `Active`; API giải phóng tài nguyên một lần.
2. Customer yêu cầu hủy Order/Ticket của mình hoặc Organizer/Admin yêu cầu hủy Event theo quyền tương ứng.
3. API kiểm tra trạng thái, quyền sở hữu và chính sách hủy đã được chốt.
4. API chuyển trạng thái hợp lệ, ghi audit/outbox nếu cần, xử lý ảnh hưởng tới Ticket/Payment và trả trạng thái cuối.
5. Request lặp không giải phóng hoặc hoàn tiền lần nữa.

**Ngoại lệ:** Không hủy đối tượng không tồn tại/không thuộc caller; không đánh dấu Order hoàn tiền nếu chưa có quy trình refund giả lập tương ứng; hủy Event phải áp dụng nhất quán tới các suất diễn và vé bị ảnh hưởng.

**Kịch bản nghiệm thu dự kiến:** hủy Hold lặp không giải phóng hai lần; Customer không hủy Order của người khác; hủy Event của Organizer A không tác động Event B; cancellation không tạo trạng thái vừa `Paid` vừa `Cancelled` ngoài quy tắc chuyển trạng thái.

### UC-09 - AI Booking Assistant

**Actor chính:** Customer đã xác thực.  
**Mục tiêu:** Tìm sự kiện, kiểm tra availability hiện tại và hỗ trợ tạo Hold theo yêu cầu của Customer.

**Điều kiện trước:** Customer có phiên đăng nhập; Assistant chỉ được gọi tool với danh tính/quyền của phiên đó. Provider/model sẽ được chốt ở giai đoạn tích hợp AI.

**Luồng chính:**

1. Customer mô tả sự kiện, thời gian, số lượng hoặc loại vé mong muốn.
2. Assistant truy vấn catalog và availability read-only, rồi nêu lựa chọn và các giả định rõ ràng.
3. Trước khi tạo Hold, Assistant tóm tắt EventSession, TicketType/ghế, số lượng, giá snapshot dự kiến và thời hạn Hold; yêu cầu Customer xác nhận rõ.
4. Sau xác nhận, Assistant gọi API Booking bằng quyền của Customer và chuyển nguyên kết quả API cho Customer.
5. Assistant chỉ nói Hold thành công khi API trả Hold thành công; nếu dữ liệu thay đổi hoặc request thất bại, Assistant trình bày lỗi và hỏi lựa chọn tiếp theo.

**Ngoại lệ/giới hạn:** Không tự checkout, thanh toán, hủy, thay đổi catalog hoặc bỏ qua kiểm tra quyền. Không giữ vé chỉ từ một yêu cầu chưa rõ/không có xác nhận. Nội dung do provider sinh ra không được dùng làm actor, `userId` hoặc bằng chứng xác nhận thay cho hành động Customer.

**Kịch bản nghiệm thu dự kiến:** Assistant chỉ đọc khi chưa có xác nhận; xác nhận tạo Hold bằng đúng danh tính Customer; availability cũ không được trình bày là kết quả giữ chỗ; không có tool thanh toán hoặc hủy được gọi.

## 5. Quyết định đã chốt và Decision Pending

### 5.1 Quyết định đã chốt

| ID | Quyết định | Cách áp dụng |
|---|---|---|
| DP-01 | Mỗi tài khoản có một role nghiệp vụ. Organizer/Admin không đồng thời mua vé như Customer. | Muốn mua vé phải dùng tài khoản Customer riêng. Điều này khớp với `users.role` hiện tại. |
| DP-02 | Mỗi hồ sơ Organizer có một owner; một Organizer có thể quản lý nhiều Event cùng lúc. Không cần cộng tác viên hoặc nhiều tài khoản owner cho cùng Organizer. | Ownership tiếp tục dựa trên `organizers.owner_user_id`; `events.organizer_id` liên kết mỗi Event với Organizer sở hữu. |
| DP-03 | Admin được hủy Hold, Order, Ticket và check-in trên toàn hệ thống, không phụ thuộc Organizer sở hữu Event. | Các thao tác thay đổi trạng thái phải được audit. Quyền hủy không tự đồng nghĩa hoàn tiền; Admin không tự đánh dấu Payment thành công. |

### 5.2 Decision Pending cần xác nhận

Các mục dưới đây chưa được quyết định. Hạn chốt theo cổng Giai đoạn 1 là **11/10/2026**. Khuyến nghị tạm thời phục vụ bản đặc tả, chưa phải quy tắc sản phẩm.

| ID | Quyết định cần chốt | Căn cứ hiện tại | Khuyến nghị tạm thời |
|---|---|---|---|
| DP-04 | Check-in được mở/đóng trong khoảng thời gian nào; quét lặp trả kết quả nào? | Ticket có `Issued`, `Used`, `Cancelled`, `Refunded`, `checked_in_at_utc`; chưa có policy giờ check-in. | Chấp nhận lần đầu theo cập nhật nguyên tử; lần sau trả trạng thái đã dùng; chốt cửa sổ thời gian theo yêu cầu địa điểm/demo. |
| DP-05 | Điều kiện hủy Order/Ticket, thời hạn, phí và refund giả lập? Organizer hủy Event ảnh hưởng giao dịch ra sao? | Phạm vi loại trừ hoàn tiền nhiều bước; schema có trạng thái `Cancelled`/`Refunded` nhưng chưa có policy. | Phân biệt hủy Hold (giải phóng vé) với hủy Order đã thanh toán (cần chính sách refund riêng); không tự coi hủy là refund. |
| DP-06 | Payment giả lập sinh success/failure theo cách nào và Order `PendingPayment` giữ chỗ đến thời điểm nào? | `Payment` hỗ trợ `Pending`, `Succeeded`, `Failed`, `Refunded`; timeout/expiry chưa chốt. | Dùng lựa chọn mô phỏng rõ ràng cho demo; expiry và race được chốt cùng quy tắc hold/order ở đầu việc nghiệp vụ sau. |

Ngoài ra, thời hạn Hold, sức chứa, giá, sale window và số vé tối đa mỗi người thuộc đầu việc số 3 của Giai đoạn 1; tài liệu này không gán giá trị cho các quy tắc đó.

## 6. Đối chiếu với baseline source

| Quan sát | Ý nghĩa cho đặc tả |
|---|---|
| `AuthenticationExtensions` đăng ký policy `Admin`, `Organizer`, `Customer`, bắt buộc `sub` và đọc role claim `roles`. | Nền xác thực/role đã có; mỗi endpoint vẫn phải thêm kiểm tra quyền trên object. |
| `users.role` giới hạn trong ba role; `organizers.owner_user_id` là unique và tham chiếu `users`; `events.organizer_id` tham chiếu `organizers`. | Ownership hiện tại là quan hệ owner → Organizer → Event; chưa có membership nhiều người. |
| API catalog hiện có `GET /api/v1/events` và chỉ chọn Event `Published`. | Đây là lát cắt đọc catalog; UC-01 mở rộng thêm detail, session, filter và availability. |
| `GET /api/v1/bookings/status` ghi rõ transactional Hold API là bước tiếp theo. | UC-03/04 là đặc tả cho implementation tương lai, không mô tả tính năng đang chạy. |
| `GET /api/v1/assistant/status` có guardrail mọi state change đi qua Booking API sau xác nhận. | UC-09 giữ nguyên nguyên tắc xác nhận và API nghiệp vụ; Assistant hiện vẫn là planned. |
| Schema đã có `holds`, `orders`, `payments`, `tickets`, `payment_webhook_events`, `audit_logs` cùng unique constraint cho một số khóa. | Có cấu trúc lưu trữ nền, nhưng schema không tự thay thế authorization, transaction flow hoặc chính sách nghiệp vụ. |

## 7. Phạm vi đã thực hiện trong tài liệu này

- Đã đặc tả actor, quyền cấp role, ownership của Organizer và quy tắc kiểm tra object-level.
- Đã viết use case catalog, quản lý catalog thuộc Organizer, Hold, checkout, Payment giả lập, Ticket, check-in, cancellation và AI Assistant.
- Đã gắn kịch bản nghiệm thu dự kiến cho từng use case để chuyển thành test khi triển khai.
- Chưa cập nhật ERD, state/sequence diagram, API contract, ADR hay chốt các quy tắc sức chứa/giá/thời gian/hủy thuộc các đầu việc tiếp theo.
