# Quy tắc nghiệp vụ Giai đoạn 1

Ngày cập nhật: **07/10/2026**. Phạm vi: sức chứa, giá, mở bán, thời hạn Hold, hạn mức vé, cancellation, payment giả lập và check-in.

**Các chính sách sản phẩm đã được người dùng xác nhận. Decision Pending GV-01:** chưa có bằng chứng giảng viên xác nhận; hạn chốt **11/10/2026**. Mọi BR bên dưới thuộc phạm vi chờ phê duyệt này. Đặc tả không khẳng định runtime đã triển khai.

## BR-01 — Sức chứa theo TicketType

Không thêm tổng `EventSession.capacity`. Mỗi TicketType là một quota thuộc một EventSession.

| Mode | Sức chứa | Nguồn quyết định |
|---|---|---|
| GeneralAdmission | `ticket_types.capacity = ticket_inventories.total_quantity` | Counter trong PostgreSQL; available = total - held - sold, tất cả không âm. |
| ReservedSeating | Capacity bằng số ghế active thuộc đúng session/TicketType | Ghế và SeatAllocation; tối đa một Held/Sold mỗi ghế. Inventory row vẫn tồn tại nhưng là projection. |

Với Reserved, `total_quantity` khớp capacity; held/sold bằng số allocation Held/Sold. Cập nhật cùng transaction với nguồn và đối soát lại từ allocation. Counter projection không được cấp quyền giữ ghế.

Hold nhiều loại vé/ghế là all-or-nothing. Không giảm GA capacity dưới held + sold; không tắt/di chuyển ghế có allocation Held/Sold hoặc vé Issued/Used. Hủy vé đã check-in vẫn giữ Sold nên cũng không được dùng để làm ghế khả dụng.

**TC-BR-01:** tranh vé GA cuối/tranh cùng ghế chỉ cấp đủ quota; lỗi một dòng rollback toàn Hold; capacity không khớp inventory/ghế bị từ chối; projection lệch không cho cấp ghế trùng; giảm quota dưới đã giữ/đã bán bị từ chối.

## BR-02 — Giá và tiền tệ

Chỉ dùng **VND**. Giá không âm, lưu `numeric(14,2)`; API biểu diễn bằng chuỗi decimal, server tính bằng decimal. Không nhận giá/tổng tiền/currency do Customer gửi để ghi nhận booking.

Tạo Hold snapshot `unit_price`, currency và quantity; checkout sao chép sang OrderItem/Order. Organizer đổi giá chỉ ảnh hưởng Hold mới. Order total là tổng quantity × unit price; Payment amount/currency phải khớp Order. Tổng vượt miền biểu diễn tiền trong DB bị từ chối trước ghi. Vé giá 0 vẫn đi qua luồng payment giả lập để nhất quán issuance.

**TC-BR-02:** giá âm/currency ngoài VND bị từ chối; đổi giá sau Hold không đổi Order/Payment; body Customer thêm price bị từ chối; kiểm tra tổng decimal chính xác và trường hợp tổng vượt miền.

## BR-03 — Cửa sổ mở bán

Tạo Hold mới yêu cầu Event Published, session chưa Cancelled/Completed và còn quota. TicketType hiện không có cột status.

`effective_start` là mốc muộn nhất trong các sale_starts_at_utc khác NULL của Event và TicketType. `effective_end` là mốc sớm nhất trong sale_ends_at_utc khác NULL và EventSession.starts_at_utc. NULL không thêm giới hạn; start inclusive, end exclusive. Giao cửa sổ rỗng không bán được. Mọi so sánh dùng UTC.

Cửa sổ này **chỉ áp dụng tạo Hold mới**. Người dùng đã xác nhận Hold còn hiệu lực được checkout/pay đủ 10 phút gốc dù đã đóng bán hoặc session bắt đầu. Event/Session cancellation vẫn chặn giao dịch. Event/session chỉ được tự hoàn tất khi đã qua giờ kết thúc và không còn reservation/Order chờ xử lý để không cắt ngắn Hold đã nhận.

Catalog availability là snapshot. Nhãn session Scheduled/OnSale/SoldOut được tính từ lịch/quota; SoldOut cũ không thể thay kiểm tra nguồn quota trong transaction.

**TC-BR-03:** ngay giờ mở nhận Hold, ngay giờ đóng từ chối; từng cửa sổ đều có hiệu lực; NULL/biên UTC xử lý nhất quán; Hold tạo trước cutoff vẫn checkout/pay trong hạn; Published nhưng session Cancelled không bán.

## BR-04 — Hold và deadline thanh toán

Hold sống **10 phút** từ `decision_time_utc` của PostgreSQL sau khi lấy các khóa cần thiết. Hợp lệ khi `now < expires_at_utc`; đúng deadline là hết hạn. Đọc/retry/checkout/attempt payment mới không gia hạn.

Checkout tạo một Order PendingPayment nhưng Hold vẫn Active, tài nguyên vẫn Held. `paymentExpiresAtUtc` của Order suy ra từ `holds.expires_at_utc`; không có deadline độc lập. Chỉ Payment success hợp lệ mới chuyển Hold Confirmed và tài nguyên Sold.

Expiry chuyển Hold Expired, pending Order Expired, pending Payment Failed và giải phóng held một lần. Mọi lệnh kiểm tra deadline ngay cả khi worker chưa chạy; mục tiêu worker giải phóng trong 60 giây sau expiry là chỉ tiêu cần đo khi triển khai.

**TC-BR-04:** sát trước/đúng deadline; chờ lock qua deadline phải bị từ chối; hai worker release một lần; checkout/retry không đổi expiry; callback/expiry cạnh tranh không để Order Paid mà chỗ bị giải phóng.

## BR-05 — Giới hạn theo TicketType trong từng Order

Áp dụng `ticket_types.max_per_order` riêng cho mỗi TicketType trong một Order, không cộng dồn giữa Order của Customer. Default schema là 10; loại vé có thể có giá trị riêng.

Server gộp các dòng cùng TicketType trước khi so tổng quantity với max_per_order. Mode phải khớp DB; seat ID không được lặp giữa các dòng. Sau canonicalization chỉ lưu một HoldItem/OrderItem cho mỗi TicketType. Với Reserved, quantity bằng số seat ID duy nhất.

Kiểm tra giới hạn tại lúc Hold được chấp nhận và lưu `max_per_order_snapshot`. Checkout giữ nguyên số lượng đã chấp nhận; thay đổi giới hạn chỉ áp dụng Hold mới. Unique `orders.hold_id` bảo đảm một Hold tối đa một Order.

**TC-BR-05:** bằng max được nhận, vượt max bị từ chối; chia một loại vé thành nhiều dòng không vượt được giới hạn; ghế trùng bị từ chối; nhiều Order đánh giá riêng; giảm max sau Hold không thay đổi lượng đã giữ.

## BR-06 — Hủy và tác động tới tài nguyên

| Actor/lệnh | Điều kiện | Tác động |
|---|---|---|
| Customer hủy Hold/Order của mình | Chưa thanh toán; Hold còn Active/hợp lệ hoặc Order PendingPayment | Hold và Order nếu có → Cancelled; pending Payment → Failed; trả held đúng một lần. |
| Admin hủy Hold/Order/Ticket | Phạm vi toàn hệ thống, reason và audit; vẫn tuân state guard | Hủy pending hoặc toàn bộ Order Paid/Ticket Issued/Used theo rule bên dưới. |
| Organizer hủy Event của mình; Admin hủy mọi Event | Event Draft/Published, reason/audit | Event Cancelled chặn mua/payment/check-in ngay; worker cleanup giao dịch/vé liên quan. |

Customer không tự hủy Order Paid/Ticket đã phát hành. Hủy riêng một Ticket giữ Order Paid; hủy toàn bộ Order Paid chuyển Order Cancelled. Payment Succeeded và các số tiền snapshot giữ nguyên. Hold Confirmed giữ lịch sử.

**Người dùng đã chốt:** vé chưa check-in được trả quota/ghế; vé đã check-in giữ lượng đã dùng. Khi Used → Cancelled, giữ `checked_in_at_utc` và Sold allocation/counter. Sau hủy vé chưa dùng, chỉ có thể bán lại nếu điều kiện mở bán vẫn hợp lệ.

Hủy Event trả 202 sau khi parent gate và outbox commit. Ticket row chưa được worker cập nhật vẫn có `admissionValid=false` vì Event Cancelled. Cleanup phải retry được và xử lý mọi booking; không tự hoàn tiền.

Hoàn tiền giả lập **ngoài phạm vi hiện tại**. Các enum Refunded trong schema chưa có workflow/API; Cancelled không đồng nghĩa Refunded. Lệnh hủy lặp không giảm counter hoặc hoàn tiền lần hai. Hủy Hold Confirmed phải qua Order/Ticket, không hồi sinh/chuyển lại Hold.

**TC-BR-06:** Customer chỉ hủy dữ liệu own chưa trả tiền; Admin xuyên Organizer có audit; hủy Issued trả sold, hủy Used giữ sold/timestamp; hủy riêng vé giữ lịch sử thanh toán; Event Cancelled chặn check-in ngay cả trước cleanup; lặp cleanup không giảm counter lần hai.

## BR-07 — Payment giả lập

Customer chọn kết quả Succeeded/Failed để demo. Command tạo Payment Pending và yêu cầu simulator qua outbox; callback dùng danh tính dịch vụ riêng. Customer/Admin không được sửa trực tiếp status thành Succeeded.

Mỗi Order tối đa một attempt Pending. Failed là terminal của attempt; retry tạo Payment mới bằng key mới nếu vẫn trong hạn Hold. Success hợp lệ commit cùng Hold Confirmed, Order Paid, allocation/counter Sold và Ticket issuance. Không phát Ticket trước đó.

Callback dùng unique (provider, provider_event_id), so hash nội dung và guard Payment/Order. Callback lặp không phát vé thêm. Cùng event ID khác payload bị conflict; callback tới sau expiry/cancel hoặc kết quả terminal trái ngược bị ignored/audit, không hồi sinh booking. Mô hình này chỉ cho Mock; cổng tiền thật cần thiết kế settlement/refund riêng.

**TC-BR-07:** failure rồi retry trong hạn; hai attempt đồng thời chỉ một Pending; callback lặp cùng/khác ID không thu/issue hai lần; lỗi giữa transaction rollback toàn bộ; callback trễ/sai amount không cấp vé.

## BR-08 — Check-in

Cho phép từ **60 phút trước starts_at_utc đến trước ends_at_utc**: khoảng `[starts_at_utc - 60 phút, ends_at_utc)`. Organizer chỉ check-in Event của mình; Admin được toàn hệ thống.

Ticket phải Issued, parent chưa Cancelled/Completed, đúng session và QR hợp lệ; nhập ticketCode thủ công bởi Organizer/Admin phải có lý do và audit riêng. Chuyển Issued → Used cùng checked_in_at_utc nguyên tử. Quét mới trên vé Used trả TICKET_ALREADY_USED. Retry cùng key trả kết quả cũ.

**TC-BR-08:** đúng giờ mở được nhận, đúng giờ đóng bị từ chối; quét đồng thời một lần Used; Organizer khác bị chặn; parent hủy chặn vé; hủy/check-in cạnh tranh giữ đúng chính sách lượng đã dùng.

## BR-09 — AI Assistant và xác nhận

AI chỉ tìm catalog/availability và đề xuất. Tạo Hold yêu cầu Customer xác nhận cụ thể session, TicketType/ghế, quantity, giá và thời hạn. Một intent có proof gắn với Customer/quote/expiry; chỉ UI có hành động xác nhận mới gọi confirm, không đưa tool confirm hay proof cho model.

Intent không giữ chỗ. Khi confirm, giá/mặt hàng đổi phải xin xác nhận mới; booking vẫn qua cùng transaction và rule như Hold thường. Không có tool AI tự checkout, payment, cancel hoặc SQL. TTL intent 5 phút là lựa chọn kỹ thuật của API, khác TTL Hold 10 phút.

**TC-BR-09:** chưa xác nhận không giữ chỗ; text từ model không tạo consent; quote đổi không tạo Hold; hết intent/retry/confirm đồng thời không tạo nhiều Hold; model không có tool trả tiền/hủy.

## Sổ quyết định

| ID cũ / liên quan | Kết luận | Xác nhận người dùng | Giảng viên |
|---|---|---|---|
| BR-01 | Quota TicketType; Reserved giữ inventory projection | Đã xác nhận trong trao đổi trước | Decision Pending GV-01, 11/10/2026 |
| BR-DP-01 → BR-02 | Chỉ VND, snapshot giá tại Hold | 07/10/2026 | Decision Pending GV-01, 11/10/2026 |
| BR-DP-02 → BR-03 | Giao sale windows; cutoff chỉ chặn Hold mới | 07/10/2026 | Decision Pending GV-01, 11/10/2026 |
| BR-DP-03 → BR-04 | 10 phút; checkout giữ deadline | 07/10/2026 | Decision Pending GV-01, 11/10/2026 |
| BR-DP-04 → BR-05 | max_per_order từng TicketType/Order | Đã xác nhận trong trao đổi trước | Decision Pending GV-01, 11/10/2026 |
| BR-DP-05 / DP-05 → BR-06 | Customer hủy trước trả tiền; Admin hủy paid; unused trả chỗ, used giữ consumption; không refund | 07/10/2026 | Decision Pending GV-01, 11/10/2026 |
| DP-06 → BR-07 | Chọn success/failure; retry trong hạn gốc | 07/10/2026 | Decision Pending GV-01, 11/10/2026 |
| DP-04 → BR-08 | Check-in [start - 60 phút, end), quét lặp báo đã dùng | 07/10/2026 | Decision Pending GV-01, 11/10/2026 |
| UC-09 → BR-09 | Xác nhận rõ trước Hold qua AI | Phạm vi use case đã xác định; proof/TTL là thiết kế kỹ thuật | Decision Pending GV-01, 11/10/2026 |

## Đối chiếu triển khai

Schema hiện có 20 bảng nền; phần lớn command nghiệp vụ chưa có. `version` hiện chỉ ở inventory; currency Hold, limit snapshot, ticket ordinal, QR metadata, idempotency ledger, inbox và cancellation job là các khoảng trống mục tiêu tại [database.md](database.md). Các rule được liên kết với state/sequence/API/ADR trong [bảng nghiệm thu](phase-1-design-index.md).

Tên thực thể thống nhất: Event, EventSession, TicketType, Hold, Order, Payment, SeatAllocation, Ticket. DB dùng snake_case; API dùng camelCase cho cùng khái niệm, có bảng ánh xạ trong API contract.

