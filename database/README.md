# TixFlow database

## Demo catalog mở rộng (DB nghiệm thu)

### Bản ghi tham khảo từ Zipevent

`demo-zipevent-reference-v1.sql` nạp 14 event có tên, khoảng ngày, venue và URL nguồn từ [danh sách Zipevent](https://www.zipeventapp.com/event/allevent) được đối chiếu ngày 10/11/2026. Chạy bằng `psql -v ON_ERROR_STOP=1 -v target_db=tixflow` (hoặc `tixflow_acceptance`). Các bản ghi có slug `zipevent-reference-01` đến `14`, `image_url` rỗng và không có ticket type/inventory; không nhập ảnh, giá, mô tả hay quyền bán vé của Zipevent. Giờ đầu/cuối trong session chỉ là mốc để biểu diễn **khoảng ngày** trên catalog, không phải lịch suất diễn chi tiết. Chạy lại không nhân đôi row.

Sau khi áp dụng migrations, chạy `demo-catalog-expanded-v1.sql` bằng `psql -v ON_ERROR_STOP=1 -v target_db=tixflow` cho stack local hoặc `-v target_db=tixflow_acceptance` cho DB nghiệm thu. Script kiểm tra tên DB khớp tham số khai báo, dùng ID/slug ổn định và có thể chạy lại mà không nhân đôi dữ liệu. Nó thêm 60 event gốc, 60 session, 60 loại vé GA và 60 inventory; không tạo order/ticket hoặc số bán giả. Ngày sự kiện cố định trong năm 2027 để kiểm thử pagination, category, venue, giá và vé miễn phí. Nếu cần làm mới theo lịch sau đó, hãy tạo phiên bản seed mới thay vì sửa dữ liệu giao dịch đã phát sinh.

Nhóm sự kiện và kiểu danh mục được tham khảo từ [danh mục công khai của Zipevent](https://web.zipeventapp.com/Event/AllEvent); tên và mô tả trong seed do TixFlow viết mới.

### Lịch demo tương lai

`demo-future-calendar-v1.sql` thêm 14 event gốc của TixFlow, mỗi tháng một event từ 06/2027 đến 07/2028. Mỗi event có một session `OnSale`, một loại vé GA và inventory, không tạo order hay số vé đã bán. Chạy sau migrations và baseline seed, với `psql -v ON_ERROR_STOP=1 -v target_db=tixflow` hoặc `target_db=tixflow_acceptance`. Script dùng ID cố định nên chạy lại không nhân đôi dữ liệu. Lịch này có mốc kết thúc 07/2028; khi cần duy trì event tương lai sau mốc đó, tạo seed version tiếp theo.

Sau khi nạp ba seed catalog vào DB local ngày 10/11/2026: 100 event tổng cộng, gồm 92 Published; 14 event mới trải đều 14 tháng. DB nghiệm thu có 102 event vì chứa thêm hai fixture phục vụ kiểm thử đặt vé.

## Phạm vi schema

Schema ban đầu có 20 bảng, chia thành các nhóm:

- Identity: `users`, `refresh_tokens`.
- Catalog: `organizers`, `venues`, `events`, `event_sessions`, `ticket_types`, `seats`.
- Booking: `ticket_inventories`, `holds`, `hold_items`, `seat_allocations`.
- Commerce: `orders`, `order_items`, `payments`, `tickets`.
- Reliability: `payment_webhook_events`, `idempotency_records`, `outbox_messages`, `audit_logs`.

`database/init/001_initial_schema.sql` được PostgreSQL Docker image chạy tự động duy nhất khi volume dữ liệu còn trống.
Các thay đổi tiếp theo nằm trong `database/migrations/002`–`009`; service `database-migrate` trong Compose áp dụng lần lượt cả với volume đã có dữ liệu. Migration 008 bổ sung snapshot checkout/payment; migration 009 bổ sung outbox lease, inbox, vé QR, check-in và mock notification của Giai đoạn 7.

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

## Xác minh Phase 0

Sau khi cấu hình `.env`, chạy kiểm tra tái lập cho cả Compose, Keycloak, API và PostgreSQL:

```powershell
.\scripts\Invoke-Phase0Smoke.ps1 -StartStack
```

Nếu chỉ cần xác minh PostgreSQL của stack đang chạy, script SQL bên dưới sẽ trả lỗi khác `0` khi baseline không đúng 20 bảng, thiếu migration Keycloak hoặc thiếu sự kiện seed đã publish:

```powershell
Get-Content .\database\phase0-smoke.sql -Raw |
    docker compose exec -T postgres sh -ec 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

Script này chỉ dành cho baseline Phase 0. Các migration thêm bảng sau này cần có kiểm tra phiên bản riêng, không thay đổi tiêu chí 20 bảng của baseline.

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
