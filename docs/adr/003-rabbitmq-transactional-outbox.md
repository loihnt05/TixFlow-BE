# ADR-003: RabbitMQ với transactional outbox

- Ngày: **07/10/2026**.
- Trạng thái: **Thiết kế đề xuất để triển khai**, kế thừa RabbitMQ/outbox trong kiến trúc nền.
- Phạm vi: sự kiện bất đồng bộ sau commit, retry, deduplication và phục hồi.

## Bối cảnh

Ghi Order rồi publish thẳng có thể mất thông điệp nếu tiến trình dừng giữa hai thao tác. Publish trước commit có thể làm consumer xử lý dữ liệu chưa tồn tại hoặc đã rollback. Transaction PostgreSQL không bao trùm RabbitMQ.

Đã có bảng/domain `outbox_messages` với payload, thời điểm, lịch thử lại, số lần thử, lỗi cuối và `processed_at_utc`; Worker hiện chỉ phát heartbeat. Chưa có publisher/consumer hoặc inbox.

## Quyết định

Command ghi thay đổi nghiệp vụ và outbox trong cùng transaction PostgreSQL. Booking, phát hành Ticket và cập nhật inventory hoàn thành tại transaction chính; không chờ RabbitMQ để trả kết quả đã commit. Consumer xử lý thông báo, cập nhật read model/cache và tác vụ phụ, không cấp lại vé từ `OrderPaid` một cách độc lập.

Mỗi message có `messageId = outbox_messages.id` ổn định qua mọi lần publish; envelope gồm `type`, `schemaVersion`, `occurredAtUtc`, `aggregateId`, `aggregateVersion`, `correlationId` và `payload`. Những trường envelope chưa có cột riêng có thể nằm trong payload theo contract versioned. Payload chỉ chứa thông tin cần thiết; không có access token, refresh token hoặc QR token thô.

### Publisher

1. Đọc batch outbox chưa xử lý và đến hạn; giành quyền xử lý bằng lease ngắn trong DB, có token của lần claim.
2. Commit claim rồi publish ngoài transaction nghiệp vụ, dùng exchange/queue durable, message persistent, `mandatory` routing và publisher confirm.
3. Chỉ đánh dấu `processed_at_utc` khi có confirm và không có unroutable return; UPDATE marker phải khớp claim token. Confirm chỉ nói broker đã nhận trách nhiệm, không chứng minh consumer hoàn tất. [RabbitMQ — Consumer acknowledgements and publisher confirms](https://www.rabbitmq.com/docs/confirms).
4. Lỗi, timeout hoặc không rõ kết quả thì giữ message để retry với cùng ID; ghi attempt/error và backoff có giới hạn. Lease hết hạn cho phép worker khác tiếp quản. Message lỗi nhiều lần cần cảnh báo và nơi xem xét/replay, không tự xóa.

Lease/claim token là **schema mục tiêu chưa có** (`claimed_until_utc`, `claim_token` hoặc bảng claim tương đương). Worker đầu tiên có thể chạy một dispatcher để giảm phức tạp, nhưng thiết kế nhiều worker vẫn phải chứng minh claim và phục hồi; không tuyên bố một `SELECT` đơn thuần bảo đảm chỉ publish một lần.

### Consumer

Consumer dùng manual acknowledgement. Với side effect ở PostgreSQL, transaction nhận message ghi inbox duy nhất theo `(consumer_name, message_id)` và cập nhật dữ liệu rồi commit; sau commit mới ack. Nếu inbox đã tồn tại, không làm side effect lần hai và ack. Inbox và side effect trong cùng database/transaction; ghi inbox trước rồi thực hiện effect ngoài transaction là chưa đủ.

Gửi email hay gọi dịch vụ ngoài cần idempotency riêng của dịch vụ hoặc một outbox tiếp theo. Inbox không tạo bảo đảm một lần duy nhất cho side effect bên ngoài transaction. Retry publish/ack có thể tạo delivery lặp, nên mục tiêu là **at-least-once delivery** với consumer idempotent. [RabbitMQ — Reliability guide](https://www.rabbitmq.com/docs/reliability).

Riêng Event cancellation là workflow nhiều transaction: consumer ghi inbox và tạo `event_cancellation_jobs` unique theo Event trong cùng transaction rồi ack. Worker của job gọi Booking command cho từng Hold/Order, quét lại phần chưa hoàn tất sau restart và chỉ đánh dấu job Completed khi cleanup xong. Không ghi inbox hoàn tất rồi xử lý một vòng lặp chỉ tồn tại trong bộ nhớ. Parent Event Cancelled chặn quyền tham dự ngay trong thời gian cleanup.

Không dựa vào thứ tự giao message giữa nhiều consumer/worker để suy ra trạng thái mới nhất. Read model dùng `aggregateVersion` để bỏ qua dữ liệu cũ hoặc đọc lại PostgreSQL khi phát hiện khoảng trống; event schema thay đổi phải giữ tương thích theo `schemaVersion`.

### Webhook payment khác inbox RabbitMQ

`payment_webhook_events` khử trùng lặp callback theo `(provider, provider_event_id)`; không dùng nó làm inbox chung. Receipt cùng ID nhưng payload nghiệp vụ khác phải bị conflict và được audit. Receipt, kết quả xử lý và chuyển trạng thái thanh toán phải commit cùng nhau. Duplicate event ID khác nhau nhưng cùng payment vẫn bị chặn bằng trạng thái/version và uniqueness nghiệp vụ.

## Phương án cân nhắc và hệ quả

| Phương án | Đánh giá |
|---|---|
| Publish trực tiếp sau SaveChanges | Không chọn vì tồn tại khoảng trống commit/publish. |
| Transaction phân tán DB + broker | Không chọn cho modular monolith khóa luận vì phức tạp vận hành. |
| Poll bảng nghiệp vụ thay outbox | Khó phân biệt chuyển trạng thái nào chưa phát; chọn outbox ghi rõ ý định phát sự kiện. |

API vẫn ghi được khi broker tạm dừng nếu DB hoạt động; queue tác vụ phụ sẽ chậm. Cần theo dõi tuổi message lâu nhất, pending count, retry/dead-letter và dung lượng DB. Retention chỉ xóa bản ghi đã xử lý sau thời gian cấu hình; inbox phải tồn tại ít nhất qua khoảng replay được hỗ trợ. At-least-once không phải bảo đảm sống sót trước mọi mất dữ liệu DB/broker; backup và topology vẫn là phần triển khai vận hành.

## Khoảng trống triển khai

- Thêm consumer inbox với unique `(consumer_name, message_id)` và timestamp UTC; bảng này không thuộc 20 bảng baseline.
- Bổ sung cơ chế claim/lease, publisher confirms/returns, manual ack, retry và xử lý message lỗi.
- Chốt envelope, routing, schema version và retention bằng cấu hình triển khai; xây metric/trace liên kết request với outbox.

## Kịch bản nghiệm thu dự kiến

| ID | Tình huống | Kết quả phải đạt |
|---|---|---|
| ADR-003-TC-01 | Crash sau commit Order và trước publish. | Outbox còn pending, dispatcher sau đó phát được. |
| ADR-003-TC-02 | Broker nhận message rồi dispatcher chết trước marker. | Có thể phát lặp cùng message ID; effect consumer không lặp. |
| ADR-003-TC-03 | Consumer commit effect rồi mất ack. | Redelivery đọc inbox và ack; effect vẫn một lần. |
| ADR-003-TC-04 | Publish không có route hoặc confirm timeout. | Không đánh dấu xử lý xong; retry/cảnh báo có dữ liệu chẩn đoán. |
| ADR-003-TC-05 | Hai dispatcher claim cùng lúc; một worker mất lease. | Claim token ngăn worker cũ sửa marker của worker mới; duplicate delivery vẫn an toàn. |
| ADR-003-TC-06 | Event version 3 tới trước version 2. | Read model không bị version 2 ghi lùi trạng thái. |
| ADR-003-TC-07 | Transaction nghiệp vụ rollback. | Không còn outbox của thay đổi chưa commit. |

