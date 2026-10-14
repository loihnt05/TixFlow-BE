# ADR-001: PostgreSQL là nguồn dữ liệu quyết định

- Ngày: **07/10/2026**.
- Trạng thái: **Thiết kế đề xuất để triển khai**, kế thừa PostgreSQL trong kiến trúc nền.
- Phạm vi: trạng thái catalog/booking/order/payment/ticket, giao dịch, idempotency và UTC.

## Bối cảnh

Nhiều API instance, expiry worker và payment callback có thể tranh cùng tài nguyên. Catalog cache có thể cũ; response có thể mất sau khi database commit. TixFlow cần quyết định một lần ai giữ ghế, ai được cấp vé và tài nguyên nào được giải phóng.

Schema PostgreSQL 16 đã có Hold, Order, Payment, Ticket, inventory, allocation, idempotency, webhook receipt, outbox và audit. Chỉ inventory hiện có `version`; Booking API hiện mới là endpoint mô tả module.

## Quyết định

PostgreSQL quyết định mọi chuyển trạng thái bền vững. Command phải đọc lại các điều kiện áp dụng, quyền sở hữu, trạng thái và expiry trong transaction. Cache, event message và dữ liệu client chỉ là đầu vào; chúng không cấp quyền giữ chỗ hoặc phát hành vé.

Thiết kế dùng transaction ngắn ở mức `READ COMMITTED`, kết hợp khóa row tường minh, UPDATE có điều kiện và unique/check constraint. Chỉ dùng `READ COMMITTED` mà không có các cơ chế này là chưa đủ cho booking. Khi UPDATE cạnh tranh, PostgreSQL kiểm tra lại điều kiện trên bản row đã được cập nhật; tính chất này hỗ trợ bộ đếm có điều kiện của General Admission. [PostgreSQL 16 — Transaction Isolation](https://www.postgresql.org/docs/16/transaction-iso.html).

### Giao dịch và thứ tự khóa

Tất cả command và worker dùng cùng thứ tự tài nguyên:

```text
Event -> EventSession -> Hold -> Order -> Payment
      -> TicketType -> Seat -> SeatAllocation -> TicketInventory -> Ticket
```

Trong mỗi nhóm, khóa theo UUID tăng dần và hoàn tất nhóm trước khi sang nhóm tiếp theo. Một transaction xử lý nhiều Hold phải khóa tất cả Hold trước khi khóa các Order liên quan. Row mới được INSERT khi đến bước phụ thuộc tương ứng. Metadata idempotency/webhook được giành quyền xử lý trước các khóa nghiệp vụ; không có luồng khóa metadata sau khi đã khóa aggregate rồi chờ transaction nắm metadata khác.

AI confirm khóa `assistant_booking_intents` sau metadata idempotency và trước Event. Lệnh catalog/cancellation không khóa ngược từ Event sang intent; consume intent và tạo Hold dùng chung transaction.

Event/EventSession được khóa `FOR SHARE` khi command chỉ kiểm tra trạng thái để ngăn một lệnh hủy/sửa lịch chạy vượt qua kiểm tra đó. Lệnh thay đổi các row này phải lấy khóa độc quyền cần thiết ngay từ đầu. Hold/Order/Payment, ghế và các row bị thay đổi dùng khóa phù hợp với cập nhật; TicketType đọc cấu hình dùng khóa chia sẻ, lệnh sửa cấu hình dùng khóa độc quyền. Không nâng khóa parent ở cuối giao dịch để tự cập nhật nhãn `SoldOut`; nhãn khả dụng được tính từ dữ liệu hoặc cập nhật bằng command có thứ tự khóa đúng.

Worker expiry chỉ đọc danh sách ID ứng viên trước; từng transaction sau đó khóa lại từ Event xuống Hold và kiểm tra lại deadline. Không `FOR UPDATE` Hold ứng viên rồi quay ngược về Event. Nếu thao tác cần nhiều tài nguyên, phải xác định tập ID trước khi giành khóa. Quan hệ parent không được thay đổi khi đã có giao dịch booking; sau khi khóa vẫn kiểm tra lại membership. PostgreSQL khuyến nghị lấy khóa theo thứ tự thống nhất để giảm deadlock; khi có deadlock, một transaction có thể bị hủy. [PostgreSQL 16 — Explicit Locking](https://www.postgresql.org/docs/16/explicit-locking.html).

### Biên commit

| Command | Dữ liệu phải commit hoặc rollback cùng nhau |
|---|---|
| Tạo Hold | Hold/HoldItem, counter GA hoặc allocation ghế, projection Reserved, snapshot, kết quả idempotency và outbox nếu phát sự kiện. |
| Checkout | Kiểm tra Hold còn hiệu lực; tạo tối đa một Order và OrderItem từ Hold; kết quả idempotency. Giữ Hold `Active` và deadline cũ. |
| Payment thành công | Webhook receipt, Payment `Succeeded`, Order `Paid`, Hold `Confirmed`, allocation `Held -> Sold`, counter `held -> sold`, Ticket, outbox và kết quả xử lý callback. |
| Expiry/hủy trước thanh toán | Hold và Order liên quan chuyển terminal, Payment đang chờ được kết thúc theo state contract, giải phóng tài nguyên một lần, audit khi cần và outbox. |
| Check-in | Ticket `Issued -> Used`, thời điểm check-in, audit và kết quả idempotency. |
| Yêu cầu hủy Event | Khóa độc quyền Event; chuyển `Cancelled`, ghi audit và `EventCancellationRequested` vào outbox; trả `202`. Worker thực hiện các transaction cleanup riêng có thể retry. |

Người dùng đã chốt các rule này ngày **07/10/2026**; xác nhận giảng viên vẫn **Decision Pending** đến **11/10/2026**. Callback mock đến sau expiry/hủy được ghi nhận là bỏ qua; không khôi phục Order hay cấp vé. Đây là gate của mô phỏng, không phải xử lý tiền đã thu ở cổng thanh toán thật.

Event `Cancelled` chặn ngay tạo Hold, checkout, payment thành công và check-in, dù worker chưa đổi tất cả Ticket. API tính `admissionValid = false` khi parent bị hủy. Cleanup được phép đọc parent đã hủy, nhưng vẫn theo thứ tự khóa chung: lấy shared Event/Session rồi xử lý từng Hold/Order/Ticket nguyên tử. Cleanup hủy Order Paid và vé liên quan, giữ Payment Succeeded/Confirmed Hold làm lịch sử; vé chưa dùng giải phóng sold, vé đã dùng giữ sold. Không gom mọi vé của Event vào transaction hủy Event đầu tiên.

Sau khi lấy đủ khóa nghiệp vụ, lấy một thời điểm quyết định từ PostgreSQL bằng `clock_timestamp()` rồi kiểm tra deadline; không dùng đồng hồ client hoặc timestamp bắt đầu transaction trước một lần chờ khóa dài. Nếu giao dịch đủ điều kiện trước deadline và giữ khóa đến commit thì expiry worker chờ rồi đọc trạng thái terminal. Sale window chỉ kiểm tra khi tạo Hold mới: Hold đang Active giữ đủ 10 phút kể cả qua giờ đóng bán hoặc session bắt đầu. Event/session chỉ hoàn tất khi qua thời điểm kết thúc và không còn reservation/Order đang chờ; không tự hoàn tất để cắt ngắn Hold hợp lệ.

Chuyển trạng thái cần điều kiện trạng thái cũ và `version` mong đợi, tăng version trong cùng UPDATE. Zero affected rows là xung đột hoặc trạng thái đã đổi; command đọc lại để phân biệt replay hợp lệ với yêu cầu bị từ chối. Tăng version của một aggregate không thay thế khóa/constraint trên tài nguyên dùng chung.

Retry lỗi transaction phải chạy lại toàn bộ transaction từ đầu bằng cùng idempotency key, có giới hạn và backoff. Không gọi broker, mô hình AI hoặc payment service trong transaction. Khi không biết commit đã thành công hay chưa, tra cứu/replay theo key; không tạo key mới để thử một lần mua nữa. Retry tự động của EF/Npgsql cần được phối hợp với execution strategy và transaction của command khi triển khai.

## Phương án cân nhắc và hệ quả

| Phương án | Đánh giá |
|---|---|
| Redis quyết định inventory rồi đồng bộ DB | Không chọn: phải giải quyết hai nguồn dữ liệu và phục hồi khi cache mất. |
| `SERIALIZABLE` cho mọi thao tác | Có thể dùng cho một số invariant phức tạp sau này; hiện chọn các invariant cụ thể và khóa/constraint để dễ chứng minh, vẫn phải retry conflict. |
| Chỉ optimistic version | Không đủ cho tạo allocation mới và nhiều aggregate dùng chung một ghế; vẫn cần unique constraint/khóa. |

Database nằm trên đường xử lý ghi và phải được giám sát lock wait/deadlock. PostgreSQL lỗi thì không cấp thêm tài nguyên từ Redis. Parent locks tạo điểm chờ với lệnh quản trị; phải giữ transaction ngắn và đo tải trước khi tối ưu thứ tự đã được chứng minh.

## Khoảng trống triển khai

- Thêm version cho các aggregate được liệt kê trong [ERD](../database.md); cập nhật mapping và predicate state/version.
- Viết command transaction, thứ tự khóa, retry và lưu kết quả idempotency nguyên tử.
- Bổ sung kiểm tra cùng session/customer, snapshot currency và constraint allocation chặt hơn; FK hiện tại chưa tự bảo đảm các quan hệ này.
- Thêm `holds.currency` cho snapshot tiền tệ; `tickets.item_ordinal` và unique `(order_item_id, item_ordinal)` bảo vệ số Ticket của mỗi dòng, kể cả GA; metadata QR key/nonce để tái dựng token mà chỉ lưu hash token.
- Idempotency cần ledger bền vững giữ request hash/resource pointer sau khi cache response hết 24 giờ; domain key dùng digest của actor/scope/key để không đụng scope trong các unique key hiện có. Schema hiện cần điều chỉnh cho cơ chế này.
- Ghi metric lock wait, số conflict, tuổi Hold quá hạn và rollback; không coi heartbeat worker là expiry worker.

## Kịch bản nghiệm thu dự kiến

| ID | Tình huống | Kết quả phải đạt |
|---|---|---|
| ADR-001-TC-01 | DB commit tạo Hold rồi mất response; client retry cùng key. | Trả cùng Hold, một lần chiếm tài nguyên. |
| ADR-001-TC-02 | Payment callback và expiry chạy cùng lúc. | Một kết quả terminal thắng; không có Order Paid mà tài nguyên đã giải phóng. |
| ADR-001-TC-03 | Lệnh chờ khóa qua deadline. | Kiểm tra giờ sau khi có khóa; không xác nhận Hold hết hạn theo giờ cũ. |
| ADR-001-TC-04 | Callback lỗi sau khi cập nhật Payment, trước khi phát Ticket. | Rollback cả bộ; không có thanh toán nội bộ thành công thiếu vé. |
| ADR-001-TC-05 | Hủy Event, check-in và booking chạy đồng thời; cleanup chưa xong. | Cùng thứ tự khóa; parent gate chặn check-in sau khi cancellation thắng, kể cả Ticket row còn Issued. |
| ADR-001-TC-06 | UPDATE dùng version cũ hoặc transaction bị deadlock. | Không ghi đè; retry có giới hạn không nhân đôi side effect. |

