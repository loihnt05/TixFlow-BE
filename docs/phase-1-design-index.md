# Bộ thiết kế và nghiệm thu Giai đoạn 1

**Thời gian kế hoạch:** 07–11/10/2026. **Cập nhật:** 07/10/2026. Phạm vi là khóa yêu cầu và thiết kế; runtime triển khai ở các giai đoạn sau.

Bộ tài liệu đã có đủ tám đầu việc. Người dùng đã xác nhận các chính sách sản phẩm trong trao đổi. **Cổng phê duyệt giảng viên còn Decision Pending GV-01, hạn 11/10/2026**; chưa đánh dấu Giai đoạn 1 đã nghiệm thu chính thức.

## Sản phẩm bàn giao

| Đầu việc | Tài liệu | Trạng thái |
|---|---|---|
| 1. Actor, quyền, ownership | [Actor và use case](phase-1-actors-and-use-cases.md) | Đã đặc tả Customer/Organizer/Admin; một role/tài khoản, một owner/nhiều Event. |
| 2. Use-case specification | [UC-01..UC-09](phase-1-actors-and-use-cases.md) | Catalog, quản lý catalog, Hold, checkout, payment, ticket, check-in, cancellation, AI. |
| 3. Quy tắc nghiệp vụ | [BR-01..BR-09](phase-1-business-rules.md) | Chính sách đã được người dùng chốt; mỗi rule có kịch bản kiểm tra dự kiến. |
| 4. ERD | [Database và ERD](database.md) | 20 bảng baseline, PK/unique/FK/version/UTC; riêng phần schema mục tiêu chưa triển khai. |
| 5. State diagrams | [6 sơ đồ trạng thái](phase-1-state-diagrams.md) | Event, Hold, Order, Payment, SeatAllocation, Ticket; kèm state guard và bảng trạng thái phối hợp. |
| 6. Sequence diagrams | [8 sequence](phase-1-sequence-diagrams.md) | Hold, tranh ghế, retry, checkout/payment, callback lặp, expiry, Event cancellation, cancel/check-in race. |
| 7. API contract | [Quy ước API](phase-1-api-contract.md), [OpenAPI JSON](api/phase-1.openapi.json) | 51 operation; auth/ownership, request/response, error/HTTP, Idempotency-Key, X-Expected-Version. |
| 8. ADR set | [Danh mục 5 ADR](adr/README.md) | PostgreSQL, Redis, RabbitMQ/outbox, Keycloak, hai booking strategy. |

## Quyết định sản phẩm đã chốt

- Quota theo TicketType; GA dùng inventory, Reserved dùng ghế/allocation và giữ inventory làm projection.
- max_per_order áp dụng riêng từng TicketType trong mỗi Order, không cộng dồn theo Customer/session.
- VND và giá snapshot tại Hold; thay đổi giá/giới hạn chỉ ảnh hưởng Hold mới.
- Hold 10 phút; checkout giữ Active và deadline gốc. Đóng bán/session bắt đầu chỉ chặn Hold mới.
- Customer tự hủy trước thanh toán; Admin hủy paid; Event cancellation vô hiệu quyền tham dự ngay.
- Hủy vé chưa check-in trả chỗ; đã check-in giữ lượng đã dùng và timestamp. Chưa có refund.
- Customer chọn success/failure cho mock, retry trong hạn bằng attempt mới; callback lặp không phát vé thêm.
- Check-in từ 60 phút trước session đến trước giờ kết thúc; quét mới trên vé Used báo đã sử dụng.
- AI chỉ tạo Hold sau xác nhận rõ, đi qua cùng Booking command.

Chi tiết xác nhận từng ID cũ và hạn phê duyệt giảng viên nằm trong [sổ quyết định](phase-1-business-rules.md).

## Truy vết rule → sơ đồ → API → kịch bản dự kiến

| Rule / phạm vi | State / Sequence | API operation chính | Kịch bản |
|---|---|---|---|
| Actor/ownership | INV-12, SQ-04/SQ-08 | management routes, createHold, checkInTicket | UC-01..09; API-TC-01/02; ADR-004-TC-01..07 |
| BR-01 capacity | INV-01/02/07, ST-A*, SQ-01/02 | createHold, replaceCapacity, replaceSeatMap | TC-BR-01; SQ-01/02-TC; ADR-005-TC-01..08 |
| BR-02 price | INV-06, SQ-01/04/05 | createHold, checkoutHold, startMockPayment | TC-BR-02; API-TC-05; DB-TC-03 |
| BR-03 sale window | INV-05, Event, SQ-01/04 | createHold, checkoutHold | TC-BR-03; API-TC-06; ADR-005-TC-12 |
| BR-04 TTL/expiry | INV-03/04/09/14, ST-H*/ST-O*, SQ-06 | checkoutHold, receiveMockCallback | TC-BR-04; SQ-06-TC; ADR-001-TC-02/03 |
| BR-05 limit | INV-15, SQ-01 | createHold, confirmBookingIntent | TC-BR-05; ADR-005-TC-04/12 |
| BR-06 cancellation | INV-10/11/13, ST-E*/ST-T*, SQ-07/08 | cancelEvent, cancelHold, cancelOrder, cancelTicket | TC-BR-06; API-TC-07; ADR-005-TC-11 |
| BR-07 mock payment | INV-06/08, ST-P*, SQ-04/05 | startMockPayment, receiveMockCallback | TC-BR-07; API-TC-09; ADR-005-TC-05/10 |
| BR-08 check-in | INV-12, ST-T*, SQ-08 | checkInTicket | TC-BR-08; API-TC-08 |
| BR-09 AI | SQ-01 reused after confirmation | assistantMessage, createBookingIntent, confirmBookingIntent | TC-BR-09; API-TC-10 |
| Idempotency/version | INV-09, SQ-03 | Mutations có header theo OpenAPI | API-TC-03/04; ADR-001-TC-01/06 |
| QR/issuance | INV-06, Ticket, SQ-05 | getTicketQr, receiveMockCallback | API-TC-11; DB-TC-02/05 |
| Outbox/recovery | SQ-01/07 | Giao dịch + worker mục tiêu | ADR-003-TC-01..07; DB-TC-06 |
| Cache | Availability read snapshot | getAvailability, listSessionSeats | ADR-002-TC-01..06 |
| Thuật ngữ/format | State enums, ERD | Mọi schema OpenAPI | API-TC-05/12 |

Đây là danh sách test **dự kiến**, chưa phải test implementation đã chạy. Kịch bản concurrency, crash và tải cần được thực hiện khi xây Booking/Worker thật.

## Cổng nghiệm thu thiết kế

- [x] Có use case và ma trận quyền; các xác nhận mới đã cập nhật vào tài liệu cũ.
- [x] Có ERD, state diagrams, sequence diagrams, API contract và ADR.
- [x] Mỗi rule có kịch bản dự kiến và đường truy vết.
- [x] Thuật ngữ và enum API/DB thống nhất; khác biệt snake_case/camelCase được ánh xạ.
- [x] Checkout không đồng thời gọi Hold Confirmed và chờ payment; Confirmed chỉ sau success.
- [x] Callback/expiry/cancellation có state guard và thứ tự khóa thống nhất; không hồi sinh booking terminal.
- [x] Hủy vé đã dùng giữ consumption, hủy vé chưa dùng trả chỗ; Payment không tự Refunded.
- [x] Event cancellation chặn hiệu lực ngay và có job cleanup bền vững.
- [x] Những bổ sung schema/API chưa triển khai được ghi rõ, không nhầm với baseline.
- [ ] **GV-01 — Giảng viên phê duyệt toàn bộ rule và bộ thiết kế trước 11/10/2026.**

## Kiểm tra tài liệu

Kiểm tra trong lượt cập nhật này tập trung vào JSON OpenAPI, liên kết nội bộ, operation ID, parameter path/header, reference schema, enum theo SQL và consistency giữa tài liệu. Các kết quả này không chứng minh API runtime đã chạy đúng hoặc hệ thống đã đạt tải dự kiến.

Kết quả kiểm tra cấu trúc ngày 07/10/2026: **51 operation, 86 schema, 788 reference, 6 request example và liên kết của 15 file Markdown hợp lệ**; enum hợp với SQL baseline. Đã rà 6 state diagram và 8 sequence diagram; `git diff --check` không báo lỗi whitespace. Đây là kiểm tra tài liệu/cấu trúc, chưa chạy bộ test implementation.

Khi giảng viên yêu cầu thay đổi một rule, cập nhật đồng thời business rules, state/sequence liên quan, OpenAPI/API prose, ERD mục tiêu và ADR chịu ảnh hưởng; giữ ID để truy vết. Tạm dừng implementation của rule chưa được chốt thay vì đưa giá trị ngầm vào code.

