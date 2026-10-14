# Giai đoạn 5 Hold và kiểm soát đồng thời

Ngày hoàn tất implementation: **13/10/2026**.

## Phạm vi đã triển khai

Giai đoạn 5 triển khai đầy đủ luồng giữ vé trong PostgreSQL cho cả hai mô hình inventory:

1. `CreateHold` nhận `Idempotency-Key`, dùng TTL mặc định 10 phút và gộp các dòng cùng TicketType trước khi kiểm tra `max_per_order`.
2. General Admission cập nhật `held_quantity` bằng `UPDATE` có điều kiện `available >= requested`; số row bị ảnh hưởng phải bằng một.
3. Reserved Seating khóa ghế theo UUID tăng dần và dựa trên partial unique index `ux_seat_allocations_active_seat` để chỉ cho phép một allocation `Held` hoặc `Sold` trên mỗi ghế.
4. Event, Session, TicketType, Inventory và Seat được khóa theo thứ tự ổn định. Transaction có `lock_timeout` mặc định 5 giây và `statement_timeout` 15 giây.
5. Hold, HoldItem, SeatAllocation và inventory counter được ghi trong cùng transaction; mọi lỗi nghiệp vụ rollback về savepoint nên không để lại Hold hoặc counter dở dang.
6. API đọc danh sách/chi tiết Hold và API hủy được bảo vệ bằng ownership. Customer chỉ đọc/hủy Hold của mình; Admin có phạm vi toàn hệ thống.
7. Cancel/expiry release inventory bằng state transition có điều kiện. Chỉ Hold `Active` được release; gọi lặp không giảm counter lần thứ hai.
8. Idempotency ledger dùng scope, user, key và SHA-256 của request canonical. Cùng key/hash replay status/body đã commit; cùng key khác payload trả `IDEMPOTENCY_KEY_REUSED`.

PostgreSQL tiếp tục là nguồn quyết định cuối cùng. Redis lock không được dùng trong Giai đoạn 5.

## API

| Method | Endpoint | Policy | Mục đích |
| --- | --- | --- | --- |
| `POST` | `/api/v1/holds` | Customer | Tạo Hold all-or-nothing |
| `GET` | `/api/v1/holds` | CustomerOrAdmin | Danh sách Hold trong phạm vi |
| `GET` | `/api/v1/holds/{holdId}` | CustomerOrAdmin | Đọc chi tiết Hold |
| `POST` | `/api/v1/holds/{holdId}/cancellations` | CustomerOrAdmin | Hủy Hold với reason |
| `DELETE` | `/api/v1/holds/{holdId}` | CustomerOrAdmin | Alias hủy; reason tùy chọn qua `X-Cancellation-Reason` |

Mọi mutation yêu cầu `Idempotency-Key`. Hủy Hold yêu cầu thêm `X-Expected-Version`. Response riêng tư dùng `Cache-Control: no-store`; replay có `Idempotency-Replayed: true`.

## Migration và cấu hình

Migration `007_phase5_hold_concurrency.sql` bổ sung:

- `holds.currency`, `holds.total_amount` và `holds.version`;
- `hold_items.max_per_order_snapshot`;
- constraint tiền, version và limit snapshot;
- index `hold_items.ticket_type_id` phục vụ lock/release.

Cấu hình `Booking` trong `appsettings.json` gồm:

- `HoldDurationMinutes`: 10;
- `LockTimeoutSeconds`: 5;
- `IdempotencyRetentionHours`: 24.

## Quy tắc transaction

Thứ tự khóa của CreateHold là Event, Session, TicketType theo ID, Inventory theo TicketType và Seat theo ID. Thời điểm quyết định và deadline lấy từ `clock_timestamp()` của PostgreSQL sau khi đã lấy các khóa tài nguyên.

General Admission và projection Reserved Seating đều tăng `held_quantity` bằng conditional update. Với Reserved Seating, allocation được ghi cùng transaction và unique index vẫn là hàng rào cuối chống giữ trùng. Khi request có nhiều loại vé hoặc nhiều ghế, lỗi tại bất kỳ item nào rollback toàn bộ thay đổi sau idempotency savepoint.

Cancel khóa Hold trước, sau đó khóa Inventory theo TicketType tăng dần, giảm `held_quantity`, chuyển allocation `Held` thành `Released` và chuyển Hold thành `Cancelled`. Nếu deadline đã tới, cùng thao tác release chuyển Hold thành `Expired` và trả `HOLD_EXPIRED`.

## Kiểm thử và bằng chứng

`HoldConcurrencyTests` kiểm tra năm nhóm:

- cùng idempotency key replay cùng Hold; payload khác bị conflict; cancel replay không release lần hai;
- 20 request tranh một ghế có đúng một winner;
- hai request có tập ghế giao nhau giữ all-or-nothing;
- 30 request, mỗi request hai vé, tranh inventory 50 vé có đúng 25 winner và `available = 0`;
- item sau thiếu capacity rollback inventory/seat của item trước và replay cùng lỗi đã lưu.

Ngày 13/10/2026, build solution đạt **0 warning, 0 error**; bộ Phase 5 đạt **5/5**; toàn bộ backend regression suite đạt **37/37** với PostgreSQL test cô lập.

## Giới hạn chuyển sang Giai đoạn 6 và 7

- Checkout, Order và Payment chưa nằm trong Giai đoạn 5.
- Worker tự quét Hold quá hạn được triển khai ở Giai đoạn 7. Trong Giai đoạn 5, thao tác cancel tại/sau deadline thực hiện lazy expiry và release.
- Chưa dùng Redis lock hoặc cache để quyết định booking.
- Load test 100, 500 và 1.000 request, metrics lock wait/deadlock và baseline hiệu năng thuộc Giai đoạn 8; test Giai đoạn 5 tập trung chứng minh invariant ở mức integration/concurrency.
