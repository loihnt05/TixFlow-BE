# ADR-002: Redis lưu dữ liệu có thể dựng lại

- Ngày: **07/10/2026**.
- Trạng thái: **Thiết kế đề xuất để triển khai**, kế thừa vai trò Redis trong kiến trúc nền.
- Liên quan: [ADR-001](001-postgresql-source-of-truth.md), [ADR-005](005-booking-strategies.md).

## Bối cảnh

Catalog và availability được đọc nhiều hơn ghi. Redis có thể giảm lượt đọc database và hỗ trợ rate limit giữa các API instance. Container Redis/AOF đã có trong Compose, nhưng chưa có cache hoặc rate limiter nghiệp vụ trong ứng dụng.

## Quyết định

Redis phục vụ cache catalog/availability, rate limit và dữ liệu tạm có thể tạo lại. PostgreSQL giữ Hold, deadline, inventory/allocation, idempotency và dữ liệu cấp vé. Một ghế hiển thị trống từ cache vẫn phải được kiểm tra trong transaction tạo Hold.

Cache dùng namespace theo môi trường và phiên bản dữ liệu; key phải bao gồm phạm vi query/session/quyền cần thiết. Không cache chung response chứa Hold, Order hoặc vé riêng của Customer. Thời gian cache là cấu hình vận hành, không phải thời hạn Hold. Response availability có thời điểm snapshot để client hiểu đây là trạng thái tại lúc đọc.

Sau DB commit, ứng dụng có thể xóa cache theo best effort; outbox phát invalidation để thử lại khi cần. Cache dùng TTL làm giới hạn dữ liệu cũ khi mất invalidation. Rebuild chỉ đọc dữ liệu đã commit từ PostgreSQL; không dựng inventory gốc từ cache. Nếu Redis lỗi, catalog dùng truy vấn DB có giới hạn/circuit breaker phù hợp; command booking vẫn kiểm tra toàn vẹn ở PostgreSQL.

Expiry worker đọc `holds.status`, `expires_at_utc` trong DB. Redis TTL hoặc keyspace notification chỉ có thể là tín hiệu đánh thức bổ sung. Redis Pub/Sub không giữ sự kiện trong thời gian subscriber mất kết nối; thông báo expired cũng không bảo đảm phát ngay tại thời điểm TTL về 0. Vì thế không dùng nó làm cơ chế duy nhất giải phóng Hold. [Redis — Keyspace notifications](https://redis.io/docs/latest/develop/pubsub/keyspace-notifications/).

Rate limit chống lạm dụng và hạn chế tải, tách khỏi `TicketType.max_per_order`. Reset Redis có thể reset cửa sổ rate limit, nhưng không reset số vé đã giữ/bán. Thiết kế triển khai phải cấu hình rõ chính sách khi rate limiter mất kết nối: endpoint công khai có thể dùng fallback hạn chế cục bộ; endpoint cần chặn tải nghiêm ngặt trả lỗi tạm thời. Không tự công bố một con số giới hạn request hoặc policy fail-open toàn hệ thống như quyết định đã được chốt.

## Phương án cân nhắc và hệ quả

| Phương án | Đánh giá |
|---|---|
| Redis là inventory chính | Không chọn vì quyết định booking phải tồn tại qua restart, eviction và mất cache. |
| Redis lock là hàng rào duy nhất chống trùng ghế | Không chọn; DB constraint vẫn là hàng rào cuối cùng, tránh phụ thuộc lease cho tính đúng. |
| Chỉ DB cho mọi truy vấn | Hợp lệ cho baseline nhỏ; có thể triển khai trước rồi bổ sung cache khi có số liệu tải. |

Availability có thể tạm cũ và client phải xử lý conflict khi đặt. Outbox invalidation lặp phải vô hại. Bật AOF không thay đổi vai trò Redis thành nguồn dữ liệu nghiệp vụ; mọi key nghiệp vụ trong cache vẫn phải có đường dựng lại.

## Khoảng trống triển khai

- Cache key/TTL, invalidation, fallback và metric hit/miss chưa có.
- Rate limiter phân tán, ngưỡng cấu hình và lỗi mất Redis chưa có.
- Expiry worker DB chưa có; không thay worker đó bằng một subscriber TTL.

## Kịch bản nghiệm thu dự kiến

| ID | Tình huống | Kết quả phải đạt |
|---|---|---|
| ADR-002-TC-01 | Xóa toàn bộ cache trong môi trường kiểm thử rồi đọc catalog. | Dựng lại được; không mất Hold, Order hoặc allocation. |
| ADR-002-TC-02 | Cache báo ghế trống nhưng DB đã Held. | Request tiếp theo bị conflict; không tạo allocation thứ hai. |
| ADR-002-TC-03 | Redis ngừng chạy quá một TTL Hold. | Worker DB vẫn hết hạn và giải phóng một lần. |
| ADR-002-TC-04 | Invalidation thất lạc/lặp hoặc cache fill cạnh tranh commit. | TTL/đọc lại hội tụ; booking không dựa trên cache để cấp vé. |
| ADR-002-TC-05 | Hai Customer đọc dữ liệu riêng qua API. | Không nhận dữ liệu riêng của nhau qua cache key chung. |
| ADR-002-TC-06 | Rate limit reset sau restart. | Hạn mức `max_per_order` và inventory trong DB vẫn được kiểm tra. |

