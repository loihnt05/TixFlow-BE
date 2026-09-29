# TixFlow database

## Phạm vi schema

Schema ban đầu có 20 bảng, chia thành các nhóm:

- Identity: `users`, `refresh_tokens`.
- Catalog: `organizers`, `venues`, `events`, `event_sessions`, `ticket_types`, `seats`.
- Booking: `ticket_inventories`, `holds`, `hold_items`, `seat_allocations`.
- Commerce: `orders`, `order_items`, `payments`, `tickets`.
- Reliability: `payment_webhook_events`, `idempotency_records`, `outbox_messages`, `audit_logs`.

`database/init/001_initial_schema.sql` được PostgreSQL Docker image chạy tự động duy nhất khi volume dữ liệu còn trống.

## Khởi tạo mới

```powershell
Copy-Item .env.example .env
docker compose up --build
```

Kiểm tra bảng và dữ liệu mẫu:

```powershell
Get-Content .\database\verify.sql -Raw |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U tixflow -d tixflow
```

## Áp dụng vào volume cũ chưa có schema

```powershell
Get-Content .\database\init\001_initial_schema.sql -Raw |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U tixflow -d tixflow
```

Không chạy lại script này khi schema đã tồn tại. Mỗi thay đổi schema sau này nên được thêm thành một script version mới, ví dụ `002_add_event_tags.sql`, và được commit cùng source code.

## Quy tắc chống overselling

Vé theo số lượng phải được giữ bằng một câu lệnh atomic:

```sql
UPDATE ticket_inventories
SET held_quantity = held_quantity + :quantity,
    version = version + 1,
    updated_at_utc = now()
WHERE ticket_type_id = :ticket_type_id
  AND total_quantity - held_quantity - sold_quantity >= :quantity;
```

Chỉ thành công khi số row cập nhật bằng `1`. Nếu bằng `0`, kho vé không đủ.

Với ghế cố định, unique partial index `ux_seat_allocations_active_seat` bảo đảm tại một thời điểm mỗi ghế chỉ có tối đa một allocation ở trạng thái `Held` hoặc `Sold`.

Toàn bộ thao tác tạo hold, hold items, cập nhật inventory và seat allocation phải nằm trong cùng một database transaction.

## Dữ liệu mẫu

- Một organizer và một customer dùng để phát triển.
- Một sự kiện đã publish.
- Một suất diễn.
- `VIP`: 10 ghế cố định.
- `Standard`: 500 vé theo số lượng.

Hai user seed có password placeholder và không dùng để đăng nhập. Module Authentication phải tạo password hash thật trước khi mở endpoint login.
