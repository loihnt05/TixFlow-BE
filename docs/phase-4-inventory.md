# Giai đoạn 4 Suất diễn ghế loại vé và inventory

## Phạm vi đã triển khai

Giai đoạn 4 hoàn thiện lớp dữ liệu và API cho cả hai mô hình bán vé:

1. `GeneralAdmission` quản lý quota bằng `ticket_inventories`.
2. `ReservedSeating` quản lý từng ghế; inventory là projection khớp số ghế active.
3. Organizer chỉ quản lý inventory thuộc sự kiện của mình; Admin có phạm vi toàn hệ thống.
4. Mọi mutation chỉ được thực hiện khi Event còn `Draft`.
5. Availability công khai là snapshot có `version`, `updatedAtUtc` và `snapshotAtUtc`; Hold ở giai đoạn 5 vẫn là quyết định cuối cùng.

`section` trên Seat biểu diễn zone/section của sơ đồ ghế. API seat-map trả danh sách đã sắp theo section, row và seat number để frontend nhóm và hiển thị mà không cần thêm bảng zone làm nguồn dữ liệu độc lập.

## Quy tắc dữ liệu

- `availableQuantity = totalQuantity - heldQuantity - soldQuantity` và không bao giờ âm.
- Không giảm capacity thấp hơn tổng held và sold.
- Capacity của ReservedSeating phải bằng số ghế active.
- Mã ghế được chuẩn hóa chữ hoa; tổ hợp session, section, row và seat number là duy nhất không phân biệt hoa thường.
- Không xóa TicketType đã có HoldItem hoặc OrderItem.
- Không xóa hoặc vô hiệu hóa Seat đang Held/Sold; Seat đã có lịch sử allocation hoặc ticket không được xóa.
- Event chỉ được publish khi mọi Session có TicketType, inventory khớp capacity và seat map ReservedSeating hợp lệ.
- TicketType và inventory có optimistic version riêng; thay đổi metadata, capacity hoặc seat map đều tăng version phù hợp.

## Migration và seed

- `005_phase4_inventory.sql` thêm version cho TicketType và index phục vụ availability/seat lookup.
- `006_phase4_inventory_seed.sql` thêm lô `FLASH50` gồm 50 vé, giới hạn hai vé mỗi đơn, dùng cho kịch bản tranh chấp và load test.
- Seed sẵn có của flash-sale event tiếp tục cung cấp 10 ghế VIP cố định và General Admission hữu hạn.

## API quản trị

- `GET/POST /api/v1/management/event-sessions/{sessionId}/ticket-types`
- `GET/PATCH/DELETE /api/v1/management/ticket-types/{ticketTypeId}`
- `PUT /api/v1/management/ticket-types/{ticketTypeId}/capacity`
- `GET/PUT /api/v1/management/ticket-types/{ticketTypeId}/seat-map`
- `POST /api/v1/management/ticket-types/{ticketTypeId}/seats`
- `GET/PUT/DELETE /api/v1/management/seats/{seatId}`

## API công khai

- `GET /api/v1/event-sessions/{sessionId}/ticket-types`
- `GET /api/v1/event-sessions/{sessionId}/availability`
- `GET /api/v1/event-sessions/{sessionId}/seats`
- `GET /api/v1/ticket-types/{ticketTypeId}/availability`

Public event detail cũng trả inventory mode, total/held/sold/available, version, thời điểm cập nhật và danh sách ghế cho ReservedSeating.

## Frontend

- Bước Tickets của form Organizer lưu tên vé, giá và quantity vào API thật; create/edit Event tiếp tục dùng nguyên bố cục hiện có.
- Trang chi tiết Event dùng available quantity từ API cho bộ đếm General Admission.
- Với ReservedSeating, cùng thẻ loại vé hiện có hiển thị lưới ghế tối thiểu và trạng thái `Available`, `Held`, `Sold`; chỉ ghế Available có thể chọn.
- Checkout vẫn là luồng mock của giai đoạn sau, nhưng URL chuyển tiếp đã mang ticket type, quantity và seat IDs đã chọn.

## Kiểm thử

`InventoryManagementTests` kiểm tra snapshot quantity/seat, flash-sale seed, ownership, capacity không được thấp hơn held + sold, chuẩn hóa mã ghế và chặn seat code trùng. Bộ `CatalogManagementTests` cũng tạo inventory thật trước khi publish để kiểm tra cổng publish mới.
