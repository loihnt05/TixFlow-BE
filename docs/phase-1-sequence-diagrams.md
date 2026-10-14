# Sequence diagrams: booking, retry, callback và expiry

Ngày thiết kế **07/10/2026**. Dùng các rule đã được người dùng xác nhận; **Decision Pending GV-01** cho xác nhận giảng viên, hạn **11/10/2026**. Đây là thiết kế, chưa phải bằng chứng runtime đã thực hiện các luồng này.

Các tên trạng thái theo [state diagrams](phase-1-state-diagrams.md); HTTP/header theo [API contract](phase-1-api-contract.md). PostgreSQL là nguồn quyết định cho mọi sequence.

## 1. Quy ước transaction và khóa

- Xác thực, kiểm tra role/ownership trước khi trả dữ liệu replay. Giành metadata idempotency/receipt trước các khóa nghiệp vụ.
- AI confirm khóa intent sau metadata và trước Event; không có command catalog/cancellation nào giữ Event rồi quay lại khóa intent. Intent đã consume không tạo Hold thứ hai.
- Đọc ID quan hệ trước để biết tập khóa; sau khi lấy khóa phải kiểm tra lại quan hệ.
- Thứ tự cố định: **Event → EventSession → Hold → Order → Payment → TicketType → Seat → SeatAllocation → TicketInventory → Ticket**; sắp xếp UUID trong từng nhóm.
- Lệnh chỉ đọc trạng thái parent dùng `FOR SHARE` Event/Session; lệnh hủy/sửa parent lấy exclusive ngay từ đầu. Không nâng shared lock sang exclusive giữa transaction.
- Mỗi lần quyết định lấy `clock_timestamp()` từ DB **sau khi chờ khóa**. Version/state/expiry/ownership được kiểm tra lại dưới khóa.
- Không gọi Redis, RabbitMQ, provider hoặc AI khi đang giữ transaction booking.
- Các lỗi trước commit rollback toàn bộ booking. Business commit và response ledger/outbox cùng một transaction; mất response không đồng nghĩa rollback.

## 2. SQ-01 — Hold thành công

```mermaid
sequenceDiagram
    actor C as Customer
    participant API
    participant DB as PostgreSQL
    participant D as Outbox dispatcher
    participant MQ as RabbitMQ
    C->>API: POST /holds + Idempotency-Key
    API->>API: JWT / Customer / normalize items và seatIds
    API->>DB: BEGIN; claim actor+scope+key, so request hash
    API->>DB: Khóa Event/Session shared; TicketType; resources theo thứ tự
    API->>DB: Lấy decision_time; kiểm tra Published, sale window, max_per_order
    API->>DB: INSERT Hold Active + items snapshot; expires = decision_time + 10 phút
    alt GeneralAdmission
        API->>DB: Guarded UPDATE held += quantity với available đủ
    else ReservedSeating
        API->>DB: Khóa Seat; kiểm tra active và allocation
        API->>DB: INSERT allocation Held; cập nhật inventory projection
    end
    API->>DB: Lưu response idempotency + outbox; COMMIT
    DB-->>API: Commit thành công
    API-->>C: 201 Hold + expiresAtUtc + version
    D->>DB: Claim outbox sau commit
    D->>MQ: Publish cùng messageId; publisher confirm
    D->>DB: Đánh dấu đã publish sau confirm
```

Hold/HoldItem tồn tại trước allocation tham chiếu chúng; allocation/counter/snapshot cùng một transaction. Mỗi TicketType được canonical thành một dòng; quantity là tổng trước khi kiểm tra max_per_order, seat ID không được lặp. Một Hold hỗn hợp GA/Reserved dùng cùng transaction: lỗi bất kỳ dòng nào rollback tất cả phần booking; ledger có thể lưu lỗi cuối cùng theo API contract.

**SQ-01-TC:** đủ quota → 201; đúng biên mở bán được nhận; đúng biên đóng bán bị từ chối; request một dòng hết vé không giữ những dòng còn lại; giảm max_per_order sau Hold không sửa lượng đã được giữ.

## 3. SQ-02 — Hai Customer tranh cùng ghế

```mermaid
sequenceDiagram
    actor A as Customer A
    actor B as Customer B
    participant APIA as API instance A
    participant APIB as API instance B
    participant DB as PostgreSQL
    A->>APIA: Hold seat S / key A
    B->>APIB: Hold seat S / key B
    APIA->>DB: BEGIN; metadata và parent gates; khóa Seat S
    APIB->>DB: BEGIN; metadata và parent gates; chờ khóa Seat S
    APIA->>DB: S trống; tạo Hold A + allocation Held
    APIA->>DB: Projection + ledger + COMMIT
    APIA-->>A: 201 Hold A
    DB-->>APIB: Có khóa S sau commit A
    APIB->>DB: Lấy giờ sau khóa; đọc lại allocation S
    DB-->>APIB: S đang Held bởi Hold A
    APIB->>DB: ROLLBACK phần booking B về savepoint; commit ledger lỗi
    APIB-->>B: 409 SEAT_UNAVAILABLE
```

Partial unique index bảo vệ allocation active ngay cả khi một đường code bỏ sót khóa. Không trả 500 cho conflict ghế dự kiến. Nếu Hold A đã quá hạn nhưng worker chưa release, B không được tự xóa riêng allocation: phải chạy expiry của toàn Hold A trong transaction riêng rồi thử lại, hoặc trả conflict để retry.

**SQ-02-TC:** nhiều instance tranh một ghế chỉ một người thành công; không có Hold rỗng do request thua; projection của Reserved không thể cấp lại ghế đã bị chiếm.

## 4. SQ-03 — Retry và mất response

```mermaid
sequenceDiagram
    actor C as Customer
    participant API
    participant DB as PostgreSQL
    C->>API: POST /holds, key K, payload P
    API->>DB: Transaction claim K + tạo Hold H + lưu response R
    API->>DB: COMMIT
    API--xC: Response 201 R bị mất trên mạng
    C->>API: Retry cùng K và P
    API->>API: Xác thực + ownership hiện tại
    API->>DB: Lookup (user, scope, K), so hash P
    DB-->>API: Completed, response R, resource H
    API-->>C: Replay 201 R; Idempotency-Replayed true
    C->>API: Cùng K nhưng payload P2 khác
    API->>DB: Hash không khớp
    API-->>C: 409 IDEMPOTENCY_KEY_REUSED
```

Nếu request đầu còn chạy, request sau chờ ngắn hoặc trả `409 REQUEST_IN_PROGRESS` với `Retry-After`; không làm mutation song song. Same-key replay trả kết quả lịch sử, nên sau đó client GET resource để đọc trạng thái mới nhất. Xác thực và quyền hiện tại luôn được áp dụng trước replay.

Response cache giữ 24 giờ theo lựa chọn kỹ thuật. Ledger tối thiểu `scope/key/hash/resource pointer/outcome` tồn tại theo retention nghiệp vụ; hết cache không cho phép lặp mua vé. Nếu không còn response gốc, trả `409 IDEMPOTENCY_RESULT_EXPIRED` kèm đường dẫn tra resource cho người có quyền; muốn một thao tác mới phải dùng key mới.

**SQ-03-TC:** response mất nhưng chỉ một Hold; retry đồng thời không side effect mới; khác payload bị từ chối; hết response cache không tạo lại booking; user bị khóa không đọc được response replay.

## 5. SQ-04 — Checkout và payment attempt

```mermaid
sequenceDiagram
    actor C as Customer
    participant API
    participant DB as PostgreSQL
    participant W as Worker / mock adapter
    C->>API: POST /holds/H/checkout + key + X-Expected-Version
    API->>DB: BEGIN; metadata; Event/Session; Hold H
    API->>DB: Kiểm tra owner, version, Active và now trước expiry
    API->>DB: INSERT Order PendingPayment + items snapshot
    API->>DB: Bump Hold version; giữ Active và expiry; ledger; COMMIT
    API-->>C: 201 Order, paymentExpiresAtUtc từ Hold
    C->>API: POST /orders/O/payments/mock, outcome, key, version
    API->>DB: BEGIN; gates và Hold/Order; kiểm tra còn hạn
    API->>DB: Tạo Payment Pending + outbox MockPaymentRequested
    API->>DB: Bump Order version; ledger; COMMIT
    API-->>C: 201 Payment Pending
    W->>DB: Đọc tác vụ sau commit
    W->>API: Callback xác thực bằng danh tính dịch vụ mock
    Note over API,DB: Callback success thực hiện SQ-05
```

Không gọi simulator trong transaction khởi tạo attempt. Một attempt Pending tại một thời điểm. Nếu attempt Failed và booking còn hạn, Customer tạo attempt mới với key mới; attempt cũ không đổi trạng thái. Cửa sổ mở bán đóng sau Hold không chặn checkout/pay; Event cancellation vẫn chặn.

**SQ-04-TC:** checkout chỉ một Order; deadline giữ nguyên; retry cùng key trả cùng Payment; key mới lúc đã có Pending trả PAYMENT_IN_PROGRESS; failure rồi retry trong TTL thành công; Organizer/Admin không đi qua quyền Customer để mua.

## 6. SQ-05 — Payment callback lặp

```mermaid
sequenceDiagram
    participant M as Mock adapter
    participant API
    participant DB as PostgreSQL
    M->>API: POST payment-webhooks/mock, event E, payment P, success
    API->>API: Xác thực danh tính dịch vụ + schema callback
    API->>DB: BEGIN; claim unique(provider,eventId), hash payload
    API->>DB: Khóa parents, Hold, Order, Payment, resources theo thứ tự
    API->>DB: Giờ sau khóa; xác minh amount/currency và trạng thái còn hạn
    API->>DB: Payment Succeeded + Order Paid + Hold Confirmed
    API->>DB: Held sang Sold; issue Ticket theo itemOrdinal
    API->>DB: Receipt processed + audit/outbox; COMMIT
    API-->>M: 200 disposition Processed
    M->>API: Gửi lại E với payload y hệt
    API->>DB: Lookup receipt; so hash; không mutation nghiệp vụ
    API-->>M: 200 disposition Duplicate
    M->>API: Event E2 khác ID nhưng vẫn success cho P
    API->>DB: P đã terminal; ghi receipt ignored
    API-->>M: 200 disposition Ignored
```

Receipt trùng ID nhưng khác payload → `409 WEBHOOK_EVENT_CONFLICT`, không ghi đè bản gốc. Callback failure trước success làm attempt Failed terminal; success sau đó bị ignored. `tickets(order_item_id,item_ordinal)` unique chống phát lại vé GA, bên cạnh khóa Order và state guard; seat unique không đủ cho GA.

Nếu callback đến muộn, command hoàn tất expiry/cancellation còn thiếu và ghi receipt ignored trong transaction phù hợp; không chuyển Payment thành Succeeded. Callback Mock không đại diện một khoản tiền thật đã thu.

**SQ-05-TC:** callback lặp cùng ID hoặc khác ID không cấp vé thêm; rollback khi lỗi giữa transaction không để Order Paid thiếu Ticket; callback sai amount/currency không đổi booking; event ID bị tái sử dụng với payload khác báo conflict.

## 7. SQ-06 — Hold expiry và payment cạnh tranh

```mermaid
sequenceDiagram
    participant W as Expiry worker
    participant API as Callback API
    participant DB as PostgreSQL
    W->>DB: SELECT ID ứng viên Active đã đến hạn, chưa khóa row
    API->>DB: Resolve parent IDs từ Payment, chưa khóa row
    alt Expiry lấy khóa và quyết định trước
        W->>DB: BEGIN; Event/Session shared; Hold SKIP LOCKED; Order/Payment/resources
        W->>DB: Lấy clock_timestamp; recheck Active và expiry
        W->>DB: Hold Expired; Order Expired; Pending Payment Failed
        W->>DB: Release Held / held giảm; outbox; COMMIT
        API->>DB: Có khóa; Hold/Order đã terminal
        API->>DB: Lưu receipt ignored; COMMIT
        API-->>API: Không issue Ticket, không hồi sinh tài nguyên
    else Callback quyết định thành công trước deadline
        API->>DB: BEGIN; gates; đầy đủ locks; giờ vẫn trước expiry
        API->>DB: Áp dụng success theo SQ-05; COMMIT
        W->>DB: Có khóa; recheck Hold đã Confirmed
        W->>DB: No-op; COMMIT
    end
```

Callback chờ khóa đến hoặc qua expiry không được dùng thời điểm nhận request trước đó để thắng. Worker chạy chậm không kéo dài quyền sử dụng Hold: mọi lệnh ghi tự kiểm tra deadline. API GET có thể thấy row Active đang chờ worker nhưng `expiresAtUtc` đã qua; client phải hiểu Hold không còn checkout được.

Mục tiêu giải phóng quá hạn trong 60 giây là chỉ tiêu vận hành cần đo ở phase triển khai. Worker restart quét lại DB, không phụ thuộc Redis TTL hay message timer đã mất.

**SQ-06-TC:** worker restart xử lý Hold cũ; hai worker không release hai lần; callback bị chặn qua deadline thất bại; Hold Confirmed không bị expiry giảm sold; nhiều Hold cùng Event không bị exclusive parent lock tuần tự hóa vô ích.

## 8. SQ-07 — Hủy Event và cleanup có thể retry

```mermaid
sequenceDiagram
    actor O as Organizer / Admin
    participant API
    participant DB as PostgreSQL
    participant Q as Outbox / RabbitMQ
    participant W as Cancellation worker
    O->>API: Cancel Event + reason + key + version
    API->>DB: BEGIN; metadata; Event exclusive; ownership/version
    API->>DB: Event Cancelled + audit + outbox EventCancellationRequested
    API->>DB: COMMIT
    API-->>O: 202 Event Cancelled
    Note over API,DB: Parent gate chặn mua / payment success / check-in ngay
    Q->>W: EventCancellationRequested (có thể lặp)
    W->>DB: Transaction inbox + durable cancellation job; COMMIT
    W-->>Q: ACK sau commit
    loop Từng booking của Event
        W->>DB: BEGIN; shared parent; Hold/Order/Payment/resources
        W->>DB: Hủy theo state; trả quota vé chưa dùng; giữ lượng đã check-in
        W->>DB: Audit/outbox; COMMIT
    end
    W->>DB: Đánh dấu job hoàn tất sau khi mọi booking xử lý
```

`event_cancellation_jobs` và inbox là schema mục tiêu. Không đánh dấu message đã xử lý trước khi có durable job. Worker chết giữa các booking thì job tiếp tục quét các row chưa xử lý; từng command đã idempotent. Event đã Cancelled không cho thêm booking nên tập cleanup hữu hạn. API không khẳng định tất cả row Ticket đã đổi status tại thời điểm trả 202; trả thêm hiệu lực tham dự suy ra từ parent.

**SQ-07-TC:** hủy Event khi broker ngừng vẫn đóng gate và outbox còn; worker chết giữa batch có thể tiếp tục; ticket row chưa cleanup vẫn không check-in; vé Used bị hủy giữ sold; không tác động Event khác.

## 9. SQ-08 — Check-in và hủy Ticket cạnh tranh

```mermaid
sequenceDiagram
    actor G as Organizer tại cổng
    actor A as Admin
    participant API
    participant DB as PostgreSQL
    G->>API: Check-in QR hoặc ticketCode
    A->>API: Cancel Ticket + reason + key + version
    API->>DB: Mỗi command theo cùng thứ tự khóa và kiểm tra parent
    alt Check-in thắng
        API->>DB: Issued -> Used; checked_in_at_utc; audit; COMMIT
        API-->>G: 200 Used
        API-->>A: 409 VERSION_CONFLICT nếu còn version cũ
        Note over A,DB: Admin GET rồi quyết định gửi lại: Used -> Cancelled giữ sold
    else Cancellation thắng
        API->>DB: Issued -> Cancelled; release Sold; audit; COMMIT
        API-->>A: 200 Cancelled
        API-->>G: 409 TICKET_INVALID
    end
```

**SQ-08-TC:** không có hai lần check-in; thao tác hủy áp dụng theo trạng thái thật dưới khóa, không theo status cũ client; replay cùng key không thực hiện lần hai.

