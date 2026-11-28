# TixFlow database

## Demo catalog mở rộng (DB nghiệm thu)

### Làm mới hai fixture đặt vé nghiệm thu

Sau khi đã migrate schema và có PostgreSQL của Docker Compose, tạo database riêng `tixflow_acceptance` rồi chạy:

```powershell
./scripts/Invoke-DemoAcceptanceRefresh.ps1 -AnchorUtc 2026-11-27T00:00:00Z
```

Script nạp `demo-acceptance-v1.sql`, sau đó `demo-acceptance-refresh-v2.sql` trên đúng `tixflow_acceptance`. Anchor UTC cố định mở cửa bán từ một ngày trước đến 30 ngày sau, đặt hai suất diễn sau 32 ngày; không reset tồn kho hay xóa giao dịch. Cùng một anchor có thể chạy lại; đổi anchor của cùng seed version hoặc refresh lần đầu khi fixture đã có Hold sẽ bị từ chối. Sau seed, script chạy `demo-acceptance-verify-v1.sql` chỉ đọc để kiểm tra số row, liên kết, cửa sổ bán/suất và tồn kho; nếu sai sẽ không ghi manifest. Mỗi lần thành công tạo `artifacts/acceptance/seed-<run-id>/manifest.json` với seed version, anchor, timezone, số lượng thực tế, invariant và SHA-256 của ba SQL. Dùng database nghiệm thu mới cho một chiến dịch khác; không chạy script này trên DB live.

Sau khi tạo 2 Customer, 2 Organizer và 1 Admin trên Keycloak test, đăng nhập từng tài khoản và đặt access token hiện hành vào các biến môi trường `TIXFLOW_CUSTOMER_A_TOKEN`, `TIXFLOW_CUSTOMER_B_TOKEN`, `TIXFLOW_ORGANIZER_A_TOKEN`, `TIXFLOW_ORGANIZER_B_TOKEN`, `TIXFLOW_ADMIN_TOKEN` trong phiên PowerShell local. Chạy `./scripts/Test-AcceptanceAccounts.ps1 -BaseUrl http://localhost:8080 -ExpectedIssuer http://localhost:8180/realms/tixflow` để đối chiếu `iss`, `aud`, hạn token, `sub`, email và role với `/users/me`, đồng thời xác nhận trạng thái Active và 5 định danh riêng biệt. Script đọc payload JWT để kiểm tra claim; chữ ký và quyền vẫn do API xác thực khi nhận token. Manifest ignored ở `artifacts/acceptance/accounts-*` chỉ lưu `id`, `sub`, email test và role, không lưu token. Đây là kiểm tra tài khoản, chưa thay cho kiểm tra ownership giữa hai Organizer/Customer qua API nghiệp vụ.

Khi mỗi Organizer đã có một event do mình sở hữu và mỗi Customer đã tạo một order qua API thật, chạy `./scripts/Test-AcceptanceOwnership.ps1 -OrganizerAEventId <uuid> -OrganizerBEventId <uuid> -CustomerAOrderId <uuid> -CustomerBOrderId <uuid>`. Có thể thêm cả cặp `-CustomerAHoldId`/`-CustomerBHoldId` và `-CustomerATicketId`/`-CustomerBTicketId` khi dữ liệu nghiệp vụ đã phát sinh. Script chỉ gọi GET: kiểm tra chủ sở hữu được 200, người còn lại bị 404, Admin đọc được event/order/hold nhưng không đọc chi tiết vé cá nhân, role không phù hợp bị 403. Với Ticket, script còn thử QR của người còn lại và đòi 404, không lưu QR. Manifest `artifacts/acceptance/ownership-*` ghi mã HTTP, ID và cờ coverage cho Hold/Ticket; thiếu cặp ID thì không coi AUTH-13 đạt đầy đủ. Không dùng event baseline cùng owner để thay cho hai event riêng biệt của Organizer A/B.

Với token Customer A và Organizer A hiện hành, chạy `./scripts/Test-AcceptanceAuthAccess.ps1 -BaseUrl http://localhost:8080` để kiểm tra 10 request GET không token, token sai cấu trúc và sai role trả đúng 401/403. Kết quả chỉ gồm route, actor và mã HTTP trong `artifacts/acceptance/auth-access-*`. Probe này không kiểm tra logout browser, refresh token hoặc JWT hết hạn; các ca đó vẫn cần E2E riêng.

### Bản ghi tham khảo từ Zipevent

`demo-zipevent-reference-v1.sql` nạp 14 event có tên, khoảng ngày, venue và URL nguồn từ [danh sách Zipevent](https://www.zipeventapp.com/event/allevent) được đối chiếu ngày 10/11/2026. Chạy bằng `psql -v ON_ERROR_STOP=1 -v target_db=tixflow` (hoặc `tixflow_acceptance`). Các bản ghi có slug `zipevent-reference-01` đến `14`, ban đầu có `image_url` rỗng và không có ticket type/inventory; không nhập giá, mô tả hay quyền bán vé của Zipevent. Giờ đầu/cuối trong session chỉ là mốc để biểu diễn **khoảng ngày** trên catalog, không phải lịch suất diễn chi tiết. Chạy lại không nhân đôi row.

`demo-zipevent-reference-v2.sql` bổ sung 35 event tham khảo được đối chiếu ngày 20/11/2026 từ [Zipevent](https://www.zipeventapp.com/) và các trang sự kiện liên kết. Chạy sau baseline seed/migrations bằng `psql -v ON_ERROR_STOP=1 -v target_db=tixflow -f database/demo-zipevent-reference-v2.sql` (hoặc `target_db=tixflow_acceptance`). Script dùng ID và slug `zipevent-reference-v2-01` đến `35` cố định, có thể chạy lại mà không nhân đôi dữ liệu. Thể loại Festival, Sports, Fashion của nguồn được chuẩn hóa thành Entertainment, Health, Art để khớp bộ lọc frontend. Session dùng 00:00–23:59 theo giờ Bangkok để biểu diễn **ngày đầu và ngày cuối** được công bố, không phải giờ diễn hay từng suất. Bản ghi chỉ dùng để xem: URL nguồn nằm trong mô tả, ban đầu không có ảnh, giá, ticket type hoặc inventory. Event có thể đổi lịch sau ngày đối chiếu; kiểm tra URL nguồn khi sử dụng lại.

Sau hai seed tham khảo, chạy `demo-zipevent-posters-v1.sql` để điền `image_url` bằng **poster dọc** cho cả 49 event. Poster được lấy từ đúng trang event Zipevent được liệt kê trong mô tả; script đối chiếu đủ 49 nguồn trước khi cập nhật và có thể chạy lại mà không ghi đè các URL không đổi. Frontend dùng poster dọc cho thẻ 4:5 và banner ngang tương ứng từ `tixflow-frontend/lib/zipevent-wide-images.ts` cho carousel. Ảnh hiển thị trực tiếp từ CDN của Zipevent, không lưu bản sao trong repository; URL có thể thay đổi theo nguồn. Với Docker Compose local:

```powershell
Get-Content .\database\demo-zipevent-posters-v1.sql -Raw |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -v target_db=tixflow -U tixflow -d tixflow
```

Với PostgreSQL local đang chạy bằng Docker Compose, có thể nạp bằng PowerShell:

```powershell
Get-Content .\database\demo-zipevent-reference-v2.sql -Raw |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -v target_db=tixflow -U tixflow -d tixflow
```

Sau khi nạp v2 vào DB local ngày 20/11/2026, catalog có 135 event, gồm 127 Published và 49 bản ghi tham khảo Zipevent (v1 + v2).

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
