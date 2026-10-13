# Giai đoạn 3 Catalog và quản trị sự kiện

Ngày cập nhật: **11/10/2026**.

## Phạm vi đã triển khai

Toàn bộ bảy đầu việc của Giai đoạn 3 đã được cài đặt:

1. CRUD cho Organizer, Venue, Event và EventSession.
2. Ownership theo chuỗi `Event/Venue -> Organizer -> OwnerUser`; Organizer chỉ truy cập dữ liệu của mình, Admin truy cập toàn hệ thống.
3. Vòng đời Event `Draft -> Published -> Cancelled/Completed`, vòng đời Session và validation thời gian bán/suất diễn.
4. Public catalog list/detail có paging, tìm kiếm và filter theo category, location, thời gian, trạng thái.
5. Organizer dashboard/form và các trang public list/detail dùng API thật, giữ nguyên cấu trúc giao diện FE.
6. Ảnh sự kiện dùng URL HTTP(S), có preview trên form và không lưu binary trong database.
7. Seed 12 sự kiện, 4 venue, session và ticket mẫu; gồm flash-sale cùng dữ liệu Published/Draft/Cancelled.

Frontend Organizer giữ nguyên cấu trúc giao diện hiện có. Trang Settings lưu Organizer profile qua API; trang Events đọc dữ liệu thật và gọi publish/cancel/delete/duplicate; form create/edit lưu Venue, Event và EventSession thay cho localStorage.

## Database

Migration `database/migrations/003_catalog_management.sql`:

- thêm `venues.organizer_id` và index ownership;
- thêm `events.version` và `event_sessions.version` để chống ghi đè khi mutation cạnh tranh;
- có thể chạy lặp an toàn.

Migration `database/migrations/004_phase3_catalog_seed.sql` thêm bộ catalog mẫu và cũng có thể chạy lặp an toàn.

Với database đã tồn tại, chạy migration trước khi khởi động phiên bản API mới. Schema khởi tạo đã được cập nhật cho môi trường mới.

## API quản trị

Các route nằm dưới `/api/v1/management` và yêu cầu policy `OrganizerOrAdmin` trừ khi ghi rõ:

- Organizer profile: `GET/POST/PUT /organizers/me`.
- Admin organizer management: `GET/POST /organizers`, `GET/PUT/DELETE /organizers/{id}`.
- Venue: `GET/POST /venues`, `GET/PUT/DELETE /venues/{id}`.
- Event: `GET/POST /events`, `GET/PATCH/DELETE /events/{id}`.
- Event transition: `POST /events/{id}/publish`, `/cancellations`, `/complete`.
- Session: `GET/POST /events/{id}/sessions`, `GET/PUT/DELETE /event-sessions/{id}`.

Event và EventSession mutation dùng trường `version`. Request dùng version cũ trả `409 VERSION_CONFLICT`.

## API công khai

- `GET /api/v1/events`: paging bằng `page`, `limit`; filter bằng `q`, `category`, `location`, `dateFrom`, `dateTo`, `status`; sort bằng `sortBy`.
- `GET /api/v1/events/{slug-or-id}`: chi tiết Event, Organizer, Venue, Session và TicketType mẫu.
- API công khai luôn áp điều kiện `Published`. Yêu cầu `status` khác Published trả tập rỗng; slug/id của Draft, Cancelled hoặc Completed trả 404.
- Thứ tự luôn có khóa phụ `Event.Id`, nhờ đó paging ổn định khi nhiều Event có cùng ngày.

## Quy tắc chính

- Event chỉ sửa hoặc xóa khi còn Draft.
- Publish yêu cầu ít nhất một session hợp lệ và session chưa kết thúc.
- Cancel chỉ từ Draft hoặc Published; trạng thái kết thúc không được mở lại.
- Complete chỉ từ Published và sau khi mọi session đã kết thúc.
- Sale end phải sau sale start; session end phải sau session start.
- Venue được chọn cho Event phải thuộc cùng Organizer.
- Xóa Organizer bị chặn khi còn Event hoặc Venue.

## Kiểm thử

`CatalogManagementTests` kiểm tra luồng Organizer -> Venue -> Event -> Session -> Publish, chặn sửa Event đã publish, chặn Organizer khác, cho phép Admin và từ chối Customer. Bộ test cũng kiểm tra validation thời gian/URL ảnh, paging ổn định, filter kết hợp, public detail và việc ẩn Event không Published. Test database dùng PostgreSQL thật qua Docker Compose.

## Chuyển sang Giai đoạn 4

TicketType/inventory trong seed hiện phục vụ catalog demo. Các trường ticket trên form Organizer vẫn giữ bố cục giao diện và sẽ được nối với API quản trị TicketType/inventory trong Giai đoạn 4.
