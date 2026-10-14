# API contract — Giai đoạn 1

Ngày **07/10/2026**. Hợp đồng mục tiêu tại [phase-1.openapi.json](api/phase-1.openapi.json), định dạng [OpenAPI 3.0.3](https://spec.openapis.org/oas/v3.0.3.html). Schema request/response, required fields, nullable, enum, auth, header và lỗi từng operation nằm trong file đó. Nội dung dưới đây chốt các quy tắc xuyên endpoint.

Chính sách sản phẩm đã được người dùng xác nhận; **Decision Pending GV-01:** giảng viên duyệt trước **11/10/2026**. API hiện tại mới có baseline catalog/identity/status; contract này chưa được cài đặt. Khi triển khai cần cập nhật backend/frontend cùng nhau vì envelope, pagination và nhiều route mới khác baseline.

## 1. Quy ước dữ liệu và HTTP

Base path `/api/v1`. Production HTTPS. JSON dùng camelCase, resource UUID, enum giữ đúng chữ hoa/thường trong schema. Response thời gian UTC RFC 3339 kết thúc bằng Z; contract nhận các trường `*AtUtc` ở dạng UTC này. Giao diện đổi sang timezone của Venue khi hiển thị.

| API | DB / ý nghĩa |
|---|---|
| eventSessionId | event_session_id; một suất diễn, không dùng tên Event thay cho session |
| ticketTypeId, inventoryMode | ticket_type_id, inventory_mode; mode từ DB quyết định strategy |
| maxPerOrder | ticket_types.max_per_order; giới hạn từng loại vé trong một Order |
| unitPrice, totalAmount | unit_price, total_amount; chuỗi decimal không âm, hai số lẻ, miền numeric(14,2) |
| currency | VND; snapshot từ Hold sang Order/Payment |
| expiresAtUtc | holds.expires_at_utc; deadline Hold |
| paymentExpiresAtUtc | Suy ra từ Hold.expires_at_utc; không có deadline Order riêng |
| version | BIGINT không âm, truyền chuỗi decimal tối đa 9223372036854775807 |
| itemOrdinal | tickets.item_ordinal mục tiêu; thứ tự đơn vị trong OrderItem |
| admissionValid | Ticket Issued và Event/Session chưa Cancelled/Completed |
| checkInAllowed | admissionValid và đang trong [session start - 60 phút, session end) |

`inventoryMode` trong request mô tả selection, phải khớp DB; không cho client chọn strategy khác để bỏ qua kiểm tra. Money tính ở server bằng decimal; không dùng float. Body có property ngoài schema bị từ chối. PATCH thiếu field nghĩa giữ nguyên; gửi NULL chỉ xóa boundary/field cho phép nullable. PUT gửi đủ trường required.

Response thành công: `{"data": ...}`; danh sách thêm `page: {limit, nextCursor}` theo schema. Cursor opaque gắn filter và thứ tự ổn định, limit mặc định 20/tối đa 100. Không trả tổng count nếu contract không yêu cầu. Header `X-Trace-Id`, `X-Server-Time-Utc` là thông tin của HTTP attempt hiện tại. Dữ liệu riêng, QR và intent dùng `Cache-Control: no-store`. 201 có Location của resource mới. CORS cần expose Location, Idempotency-Replayed, X-Trace-Id, X-Server-Time-Utc và Retry-After cho origin frontend được phép.

## 2. Danh tính và ownership

Human Bearer JWT được Keycloak xác thực issuer/audience/signature/lifetime; phải có đúng một application role Customer/Organizer/Admin và tài khoản local Active. `sub` ánh xạ `users.identity_subject` sang `users.id`. Không dùng userId/owner/role trong body làm bằng chứng quyền.

- Customer: Hold/Order/Payment/Ticket own; chỉ Customer tạo Hold/checkout/mock payment/AI booking intent.
- Organizer: catalog/check-in thuộc chuỗi Event.organizer_id → Organizer.owner_user_id. Một owner nhiều Event; không có thành viên cộng tác.
- Admin: quản lý catalog, đọc hỗ trợ và hủy/check-in toàn hệ thống; không kế thừa quyền mua vé Customer. Đọc giao dịch/QR hỗ trợ có supportReason; thay đổi có audit.
- Route công khai chỉ trả Event Published và tài nguyên con được công khai. Route management kiểm tra ownership kể cả với UUID hợp lệ.
- Sai role trả 403; ID không tồn tại hoặc nằm ngoài phạm vi ownership trả 404 để không lộ tài nguyên. Điều kiện state/version vẫn áp dụng Admin.

Callback Mock dùng Bearer **dịch vụ riêng**, audience `tixflow-mock-callback`, scope `payment:callback`; human token không được gọi. Đây là cấu hình cần thêm, không sử dụng quyền Customer để giả mạo callback.

## 3. Idempotency

Mọi mutation thông thường yêu cầu `Idempotency-Key`: 1–120 ký tự ASCII hiển thị không có khoảng trắng (0x21–0x7E). POST assistant/messages chỉ suy luận đọc nên không cần key; webhook dùng provider event identity.

Scope = `(local_user_id, operationId + ":" + targetResourceId hoặc "collection", client_key)`. Request hash SHA-256 trên method, route canonical, body semantic canonical và X-Expected-Version nếu có. JSON property được sắp ổn định; UUID chuẩn hóa, selection gộp theo TicketType, seat IDs sắp và kiểm tra trùng. Query có tác động semantics cũng phải nằm trong hash. Cùng key ở operation/resource khác là scope khác.

1. Xác thực/ownership hiện tại trước mọi replay. Claim metadata unique trong PostgreSQL, rồi thực hiện command.
2. Key + hash trùng, completed: trả **cùng HTTP status và body đã commit**, `Idempotency-Replayed: true`; không chạy mutation, không kiểm tra version cũ để từ chối replay.
3. Key trùng nhưng hash khác: 409 IDEMPOTENCY_KEY_REUSED.
4. Key đang được transaction khác xử lý: chờ có giới hạn, sau đó 409 REQUEST_IN_PROGRESS, Retry-After: 1. Không tạo mutation thứ hai.
5. 2xx và lỗi nghiệp vụ cuối cùng 409/422 sau claim được lưu làm outcome. Với lỗi cần rollback booking, rollback phần booking về savepoint rồi commit ledger lỗi; không để lại giữ chỗ một phần. Lỗi auth/shape trước claim và lỗi hạ tầng làm rollback transaction không được lưu như kết quả nghiệp vụ hoàn tất.
6. DB commit nhưng mất response: client retry **cùng key/request gốc**. Không tạo key mới chỉ vì mạng timeout.
7. Response cache 24 giờ là lựa chọn kỹ thuật. Ledger hash/resource pointer/outcome còn theo retention nghiệp vụ; quá cache trả 409 IDEMPOTENCY_RESULT_EXPIRED kèm resourceUri trong phạm vi quyền. Không tái dùng key để mua lại.
8. Không lưu plaintext QR/proof trong ledger. Response intent bỏ token khi lưu và tái tạo proof đúng metadata lúc replay; timestamp/expiry gốc không đổi. GET QR không đi qua ledger mutation.

Domain `holds.idempotency_key` và `payments.idempotency_key` nhận digest từ actor/scope/client key để khớp unique hiện tại. Ledger lưu scope và client key riêng để kiểm tra request hash. Client GET resource sau replay để biết trạng thái hiện tại.

Checkout còn có natural uniqueness theo Hold: nếu đã có Order, key mới vẫn trả 200 Order hiện có, không tạo lại. Sau authorization, kiểm tra liên kết này trước version/deadline vì không có mutation booking. Same-key replay giữ status gốc 201 hoặc 200. Hủy một đối tượng đã Cancelled là no-op, trả trạng thái hiện có (Event cancellation vẫn 202); không giảm counter hoặc tăng version lần nữa.

## 4. Version và tranh chấp

GET resource trả `data.version` dạng chuỗi. Mutation cần optimistic concurrency gửi `X-Expected-Version: 7` (không có dấu ngoặc kép trong giá trị header). Thiếu/sai header trả 400; version khác trả **409 VERSION_CONFLICT**. Client GET lại, hiển thị dữ liệu mới và gửi command mới với key mới khi muốn tiếp tục.

Header áp vào aggregate được chỉ rõ bởi operation: create Session dùng Event version; create TicketType dùng Session version; thay seat map/capacity dùng TicketType version; checkout dùng Hold version; tạo Payment dùng Order version. Những lệnh đó tăng version aggregate tương ứng. Sửa riêng row khác không tùy tiện tăng version parent.

Thứ tự trả lỗi: xác thực/role và shape header/body → ownership → lookup/replay key → natural replay/no-op đã nêu → version → state/deadline/quan hệ nghiệp vụ dưới khóa. Vì vậy request vừa có version cũ vừa có Hold hết hạn có thể nhận VERSION_CONFLICT trước; client GET lại sẽ thấy trạng thái/deadline mới. Callback không có client version; parent bị hủy được ưu tiên xử lý như cancellation trước deadline cleanup.

Không dùng version làm ETag vì response có thể chứa dữ liệu suy ra theo thời gian hoặc parent. Check-in và callback không đòi client version: scanner/provider không có resource snapshot, server lấy khóa/CAS trạng thái/version thật. Tất cả dùng thứ tự khóa trong [ADR-001](adr/001-postgresql-source-of-truth.md).

Xung đột optimistic version nội bộ của inventory được retry cả transaction có giới hạn; không buộc Customer cung cấp inventory version. Khi retry hạ tầng đã hết, trả 503 DEPENDENCY_UNAVAILABLE cùng Retry-After; VERSION_CONFLICT dành cho precondition aggregate của caller.

## 5. Danh mục endpoint

Roles dưới đây bổ sung Bearer security trong OpenAPI. Organizer luôn own; Customer luôn own cho tài nguyên giao dịch; Admin có phạm vi hệ thống và audit. Bảng dùng đường dẫn tương đối dưới /api/v1.

| Method | Route | Role | Thành công |
|---|---|---|---|
| GET | `/events` | Công khai | 200 |
| GET | `/events/{eventId}` | Công khai | 200 |
| GET | `/events/{eventId}/sessions` | Công khai | 200 |
| GET | `/event-sessions/{sessionId}/ticket-types` | Công khai | 200 |
| GET | `/event-sessions/{sessionId}/availability` | Công khai | 200 |
| GET | `/event-sessions/{sessionId}/seats` | Công khai | 200 |
| GET | `/venues` | Công khai | 200 |
| GET | `/management/organizers/me` | Organizer | 200 |
| POST | `/management/organizers/me` | Organizer | 201 |
| PUT | `/management/organizers/me` | Organizer | 200 |
| GET | `/management/organizers/{organizerId}` | Admin | 200 |
| PUT | `/management/organizers/{organizerId}` | Admin | 200 |
| GET | `/management/events` | Organizer, Admin | 200 |
| POST | `/management/events` | Organizer, Admin | 201 |
| GET | `/management/events/{eventId}` | Organizer, Admin | 200 |
| PATCH | `/management/events/{eventId}` | Organizer, Admin | 200 |
| POST | `/management/events/{eventId}/publish` | Organizer, Admin | 200 |
| POST | `/management/events/{eventId}/cancellations` | Organizer, Admin | 202 |
| POST | `/management/events/{eventId}/sessions` | Organizer, Admin | 201 |
| GET | `/management/events/{eventId}/sessions` | Organizer, Admin | 200 |
| GET | `/management/event-sessions/{sessionId}` | Organizer, Admin | 200 |
| PUT | `/management/event-sessions/{sessionId}` | Organizer, Admin | 200 |
| POST | `/management/event-sessions/{sessionId}/ticket-types` | Organizer, Admin | 201 |
| GET | `/management/event-sessions/{sessionId}/ticket-types` | Organizer, Admin | 200 |
| GET | `/management/ticket-types/{ticketTypeId}` | Organizer, Admin | 200 |
| PATCH | `/management/ticket-types/{ticketTypeId}` | Organizer, Admin | 200 |
| PUT | `/management/ticket-types/{ticketTypeId}/capacity` | Organizer, Admin | 200 |
| GET | `/management/ticket-types/{ticketTypeId}/seat-map` | Organizer, Admin | 200 |
| PUT | `/management/ticket-types/{ticketTypeId}/seat-map` | Organizer, Admin | 200 |
| GET | `/management/event-sessions/{sessionId}/sales-summary` | Organizer, Admin | 200 |
| GET | `/management/event-sessions/{sessionId}/tickets` | Organizer, Admin | 200 |
| POST | `/holds` | Customer | 201 |
| GET | `/holds` | Customer, Admin | 200 |
| GET | `/holds/{holdId}` | Customer, Admin | 200 |
| POST | `/holds/{holdId}/cancellations` | Customer, Admin | 200 |
| POST | `/holds/{holdId}/checkout` | Customer | 200/201 |
| GET | `/orders` | Customer, Admin | 200 |
| GET | `/orders/{orderId}` | Customer, Admin | 200 |
| POST | `/orders/{orderId}/cancellations` | Customer, Admin | 200 |
| POST | `/orders/{orderId}/payments/mock` | Customer | 201 |
| GET | `/payments/{paymentId}` | Customer, Admin | 200 |
| POST | `/payment-webhooks/mock` | Mock service | 200 |
| GET | `/tickets` | Customer, Admin | 200 |
| GET | `/tickets/{ticketId}` | Customer, Admin | 200 |
| GET | `/tickets/{ticketId}/qr` | Customer, Admin | 200 |
| POST | `/tickets/{ticketId}/cancellations` | Admin | 200 |
| POST | `/check-ins` | Organizer, Admin | 200 |
| POST | `/assistant/messages` | Customer, Organizer, Admin | 200 |
| POST | `/assistant/booking-intents` | Customer | 201 |
| GET | `/assistant/booking-intents/{intentId}` | Customer | 200 |
| POST | `/assistant/booking-intents/{intentId}/confirm` | Customer | 201 |

`GET /users/me`, access probes, system/info, health và Swagger là baseline kỹ thuật được giữ trong source; không phải hợp đồng booking mới của bảng trên. CRUD Venue/role provisioning và refund không được bổ sung vào phạm vi này.

## 6. Request và response trọng tâm

### Hold

```http
POST /api/v1/holds
Authorization: Bearer <access-token>
Idempotency-Key: booking-20261007-001
Content-Type: application/json

{
  "eventSessionId": "10000000-0000-4000-8000-000000000001",
  "items": [
    {
      "ticketTypeId": "20000000-0000-4000-8000-000000000001",
      "inventoryMode": "ReservedSeating",
      "quantity": 2,
      "seatIds": [
        "30000000-0000-4000-8000-000000000001",
        "30000000-0000-4000-8000-000000000002"
      ]
    }
  ]
}
```

Vé GA gửi quantity và không có seatIds. Các loại vé cùng session; gộp dòng trùng type rồi kiểm tra max_per_order, seat không lặp. Response 201 theo HoldResponse chứa id/items/currency/totalAmount/expiresAtUtc/orderId/version. `maxPerOrderSnapshot` ghi giá trị được chấp nhận, không áp lại limit mới ở checkout.

### Checkout và payment

Checkout `POST /holds/{holdId}/checkout` không body, có key + X-Expected-Version Hold. 201 khi mới tạo, 200 khi Order đã tồn tại; không giảm inventory thêm. Hold vẫn Active, allocation Held.

`POST /orders/{orderId}/payments/mock` có body `{"outcome":"Succeeded"}` hoặc `{"outcome":"Failed"}`, key và Order version. 201 Payment Pending cùng outbox simulator request; client GET Payment/Order để theo dõi. Nếu đang có attempt Pending, key mới trả PAYMENT_IN_PROGRESS. Failed retry tạo Payment mới trong deadline cũ.

Order trả `latestPaymentId` để UI khôi phục trạng thái attempt sau khi tải lại; Payment trả `failureReason` (MOCK_DECLINED, ORDER_EXPIRED, ORDER_CANCELLED hoặc EVENT_CANCELLED) khi Failed. Mã PAYMENT_IN_PROGRESS có thể trả resourceUri của Payment đang xử lý trong phạm vi quyền.

Callback body theo Webhook: providerEventId, paymentId, providerTransactionId, outcome, amount, currency, occurredAtUtc. Server không tin occurredAtUtc để vượt deadline; dùng DB clock sau khóa. Receipt trả `disposition: Processed | Duplicate | Ignored`, reason nullable. Success chỉ được chấp nhận nếu attempt Pending, Order PendingPayment, Hold Active/chưa expiry và parent chưa hủy/hoàn tất.

### Hủy và check-in

Cancellation body `{"reason":"Lý do thao tác"}`, key + version đúng aggregate. Customer hủy chưa trả tiền; Ticket cancellation chỉ Admin. Cancel Paid Order hủy toàn bộ Ticket, giữ tiền/history; riêng Ticket cancellation giữ Order Paid. Used bị hủy vẫn giữ Sold/checked-in timestamp.

Event cancel 202 chỉ xác nhận gate + outbox đã commit. Inbox tạo durable cancellation job, worker cleanup qua các command idempotent. Ticket API trả admissionValid=false ngay theo parent, không cần chờ row Ticket đổi sang Cancelled.

Check-in dùng **một trong hai** body:

```json
{
  "eventSessionId": "10000000-0000-4000-8000-000000000001",
  "qrToken": "<opaque-token>"
}
```

```json
{
  "eventSessionId": "10000000-0000-4000-8000-000000000001",
  "ticketCode": "TF-EXAMPLE",
  "reason": "Kiểm tra thủ công tại cổng"
}
```

Admin cần reason cả khi quét QR; manual ticketCode luôn có reason. Chỉ staff có role/ownership hợp lệ được manual check-in; audit ghi phương thức và actor. 200 Used khi lần đầu, 409 TICKET_ALREADY_USED khi quét mới lại vé Used. Sai session/parent/vé hủy trả TICKET_INVALID hoặc RESOURCE_NOT_FOUND theo quyền.

### QR hiển thị lại

Chỉ có hash thì không tái tạo được random token gốc. Thiết kế dùng token opaque HMAC với domain/version, Ticket.id, qr_nonce và key bí mật chỉ có ở server; lưu key ID/nonce và SHA-256 của token, không lưu raw token. So sánh hash bằng thao tác constant-time; key cũ phải còn cho vé đang hiệu lực. Đổi key không cho phép vô hiệu vé ngầm.

GET QR chỉ cho Customer owner hoặc Admin hỗ trợ có lý do/audit; Organizer không được tải QR của khách. Hiển thị QR không yêu cầu đang trong cửa sổ check-in, nhưng phải còn quyền tham dự. Không trả token trong danh sách Ticket, log, outbox hoặc lỗi.

### AI confirmation

assistant/messages chỉ trả văn bản/đề xuất, không tạo booking. UI Customer tạo booking intent từ selection; server tính quote và proof sống 5 phút, không giữ quota. Proof ký bằng key/domain tách biệt QR, gắn user/intent/quote/expiry; không đưa vào prompt/tool context của model.

Sau thao tác xác nhận rõ của người dùng, UI gọi confirm với confirmed=true, quoteHash, confirmationToken và key. Quote thay đổi → QUOTE_CHANGED, cần intent/xác nhận mới. Consume intent, tạo Hold bằng Booking command và lưu ledger cùng transaction; lỗi rollback không tiêu thụ intent. Same-key replay trả cùng Hold. Intent đã consume với key khác không tạo Hold mới.

## 7. Error contract

Dùng `application/problem+json` theo [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457.html), bổ sung code, traceId, retryable, errors; client phân nhánh theo code, không parse chuỗi detail.

```json
{
  "type": "/problems/seat-unavailable",
  "title": "Ghế không còn khả dụng",
  "status": 409,
  "detail": "Một hoặc nhiều ghế đã được giữ hoặc bán.",
  "instance": "/api/v1/holds",
  "code": "SEAT_UNAVAILABLE",
  "traceId": "example-trace",
  "retryable": false
}
```

| HTTP | Code | Hành vi client / ý nghĩa |
|---|---|---|
| 400 | VALIDATION_ERROR | Sai JSON/shape/format/field không hỗ trợ; sửa input. |
| 400 | IDEMPOTENCY_KEY_REQUIRED, EXPECTED_VERSION_REQUIRED | Thiếu header bắt buộc; không có mutation. |
| 401 | AUTHENTICATION_REQUIRED | Đăng nhập/làm mới token; Bearer challenge. |
| 403 | FORBIDDEN, ACCOUNT_INACTIVE, INVALID_ROLE_SET | Sai role, tài khoản bị khóa, token không có đúng một business role. |
| 404 | RESOURCE_NOT_FOUND | Không tồn tại hoặc ngoài ownership, không lộ dữ liệu. |
| 409 | VERSION_CONFLICT, INVALID_STATE | GET lại resource; chỉ gửi command mới hợp lệ với key mới. |
| 409 | IDEMPOTENCY_KEY_REUSED | Cùng scope/key khác request; không lặp mutation. |
| 409 | REQUEST_IN_PROGRESS | Request gốc còn xử lý; Retry-After và cùng key/request. |
| 409 | IDEMPOTENCY_RESULT_EXPIRED | Cache response hết hạn; tra resourceUri, không tạo lại bằng key cũ. |
| 409 | EVENT_NOT_ON_SALE, SALES_WINDOW_CLOSED | Không nhận Hold mới hoặc parent bị hủy/hoàn tất. Không dùng đóng bán để từ chối Hold cũ còn hạn. |
| 409 | SEAT_UNAVAILABLE, INSUFFICIENT_CAPACITY | Chọn ghế/số lượng khác; không có Hold một phần. |
| 409 | CAPACITY_CONFLICT | Không giảm quota/tắt ghế đã chiếm. |
| 409 | HOLD_EXPIRED, ORDER_EXPIRED | Tạo lựa chọn/Hold mới nếu còn bán; không gia hạn bản cũ. |
| 409 | PAYMENT_IN_PROGRESS, PAYMENT_ALREADY_SUCCEEDED | Đọc Payment/Order hiện có; không tạo attempt song song. |
| 409 | WEBHOOK_EVENT_CONFLICT | Cùng provider event ID khác nội dung; provider phải kiểm tra, không ghi đè receipt. |
| 409 | TICKET_ALREADY_USED, TICKET_INVALID, CHECK_IN_WINDOW_CLOSED | Không mở cổng; hiển thị lý do phù hợp quyền. |
| 409 | CONFIRMATION_EXPIRED, QUOTE_CHANGED | Lấy quote/intent mới và yêu cầu xác nhận lại. |
| 422 | ORDER_LIMIT_EXCEEDED, DUPLICATE_SEAT, INVENTORY_MODE_MISMATCH | Payload đúng shape nhưng lựa chọn vi phạm quan hệ/giới hạn nghiệp vụ. |
| 422 | PAYMENT_AMOUNT_MISMATCH, CONFIRMATION_REQUIRED | Callback amount/currency sai hoặc proof/consent không hợp lệ. |
| 422 | VALIDATION_ERROR | Quan hệ/thời gian/tổng tiền không hợp lệ sau khi parse đúng schema; lỗi field trong errors. |
| 409 | PROFILE_CONFLICT | Subject/email bị xung đột khi đồng bộ hồ sơ, không chiếm dữ liệu cũ. |
| 409 | RESOURCE_CONFLICT | Slug/code/vị trí ghế hoặc hồ sơ Organizer trùng unique key. |
| 503 | POLICY_NOT_FINALIZED | Deployment chưa bật policy đã được duyệt; trả decisionIds. Mã dự phòng, không biểu thị người dùng chưa trả lời các rule hiện tại. |
| 429 | RATE_LIMITED | Retry-After, không đổi key khi chưa biết kết quả. |
| 503 | DEPENDENCY_UNAVAILABLE | Hạ tầng tạm lỗi; retry cùng key sau Retry-After. |
| 500 | INTERNAL_ERROR | Không lộ SQL/stack/secret; tra traceId, giữ key khi không biết commit. |

`x-error-codes` trong OpenAPI liệt kê lỗi áp dụng từng operation. Lỗi hạ tầng không được trả SEAT_UNAVAILABLE để che mất nguyên nhân. Rate limit/circuit breaker là cấu hình kỹ thuật, chưa đặt con số yêu cầu/giây ở Phase 1.

## 8. Kịch bản nghiệm thu API dự kiến

| ID | Kiểm tra |
|---|---|
| API-TC-01 | Mọi private route enforce role + ownership; Organizer A không thao tác Event B; Admin không mua bằng Customer route. |
| API-TC-02 | Token nhiều business role bị chặn, kể cả local primary role chọn một giá trị. |
| API-TC-03 | Same key/hash replay; key khác body/version conflict; cache response hết hạn không lặp booking. |
| API-TC-04 | X-Expected-Version thiếu/cũ, concurrent mutation không ghi đè. |
| API-TC-05 | Giá client/currency/owner fields ngoài schema bị từ chối; tổng decimal và UTC boundary đúng. |
| API-TC-06 | Checkout/payment sau đóng bán vẫn dùng Hold còn hạn; tại expiry bị chặn. |
| API-TC-07 | Event cancellation202 chặn admission ngay; job restart vẫn cleanup đủ. |
| API-TC-08 | Check-in QR/manual, wrong owner/session, đầu/cuối cửa sổ và quét đồng thời. |
| API-TC-09 | Webhook trùng/khác payload/late; không phát vé hoặc thu hai lần. |
| API-TC-10 | AI chưa consent/proof hết hạn/quote đổi không tạo Hold; confirm replay chỉ một Hold. |
| API-TC-11 | GET QR owner/Admin audited; Organizer không có raw token; đổi key không mất vé active. |
| API-TC-12 | Pagination cursor gắn filter, status, nullable và field casing đúng OpenAPI. |

Đối chiếu [ERD và schema mục tiêu](database.md), [state diagrams](phase-1-state-diagrams.md), [sequence diagrams](phase-1-sequence-diagrams.md). Chỉ tạo migration/implementation sau khi áp dụng bộ quyết định và cổng phê duyệt tương ứng.

