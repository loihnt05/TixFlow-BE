# TixFlow API — hướng dẫn tích hợp

> Cập nhật theo Swagger của stack đang chạy ngày **09/11/2026** (UTC+07:00).  
> Swagger đang chạy: `http://localhost:8080/swagger/v1/swagger.json` — đây là nguồn tham chiếu cho api endpoint và schema hiện tại.

## 1. Phạm vi và cách đọc tài liệu

Tài liệu này mô tả **API đang được triển khai**: base URL, xác thực, định dạng dữ liệu, endpoint và luồng gọi chính. Nó không thay thế OpenAPI/Swagger.

Hai tài liệu dễ bị nhầm có mục đích khác nhau:

| Tài liệu | Vai trò |
|---|---|
| `docs/api-huong-dan-tich-hop.md` (file này) | Hướng dẫn tích hợp thực tế, ngắn gọn và bằng tiếng Việt. |
| `docs/api-response-contract.md` | Quy ước envelope `ApiResponse`/`ApiErrorResponse`; chưa áp dụng tự động cho mọi endpoint cũ. |
| `docs/phase-1-api-contract.md` | Hợp đồng **mục tiêu** của Phase 1; có endpoint/trạng thái chưa chắc đã giống runtime hiện tại. |
| `http://localhost:8080/swagger` | Schema và endpoint của API đang chạy; dùng khi code client hoặc kiểm thử. |

## 2. Kết nối nhanh

| Môi trường local | Giá trị |
|---|---|
| API base URL | `http://localhost:8080` |
| API version prefix | `/api/v1` |
| Swagger UI (Development) | `http://localhost:8080/swagger` |
| OpenAPI JSON | `http://localhost:8080/swagger/v1/swagger.json` |
| Health | `GET /health` |
| Frontend origin mặc định | `http://localhost:3000` |
| Identity/OIDC realm local | `http://localhost:8180/realms/tixflow` |

Frontend lấy API base URL từ `NEXT_PUBLIC_API_URL`. Không hard-code URL vào component; dùng `apiBaseUrl()` trong `tixflow-frontend/lib/public-config.ts` hoặc `useApi()` trong `tixflow-frontend/lib/api.ts`.

Ví dụ kiểm tra kết nối:

```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/v1/events?page=1\&limit=2
```

## 3. Quy ước HTTP chung

### Xác thực và quyền

- Endpoint công khai không cần `Authorization`: health, system/info, catalog Event công khai, availability và một số status endpoint.
- Endpoint riêng dùng `Authorization: Bearer <access-token>` do Keycloak cấp.
- Các policy hiện có gồm `Customer`, `Organizer`, `Admin` và các tổ hợp `CustomerOrAdmin`, `OrganizerOrAdmin`; mock payment webhook dùng service policy riêng.
- Không gửi `userId`, `ownerId` hay role trong body để chứng minh quyền. Server suy actor từ token và local profile.

### Header quan trọng

| Header | Dùng khi | Ý nghĩa |
|---|---|---|
| `Authorization` | Route private | `Bearer <access-token>`. |
| `Content-Type: application/json` | Request có body | Gửi JSON camelCase. |
| `Idempotency-Key` | Tạo Hold, checkout, payment mock, check-in và mutation được yêu cầu | Key ASCII không khoảng trắng, 1–120 ký tự. Gửi lại **cùng key và cùng request** khi không chắc kết quả đã commit. |
| `X-Expected-Version` | Checkout/payment hoặc mutation dùng optimistic concurrency | Version mà client vừa đọc; version cũ có thể nhận `409 VERSION_CONFLICT`. |
| `X-Correlation-ID` | Mọi response | Do server sinh; dùng khi tra log/sự cố. |
| `Idempotency-Replayed: true` | Response replay | Server trả lại kết quả đã lưu, không thực hiện mutation lần hai. |

### Response và lỗi

Phần lớn endpoint mới trả thành công theo dạng `{"data": ...}`; catalog public là một ngoại lệ đang dùng page trực tiếp. Vì đây chưa phải global middleware, hãy xem schema từng endpoint trong Swagger thay vì giả định mọi response đều có `data`.

Lỗi dùng `application/problem+json` khi endpoint có problem response. Client cần phân nhánh theo `code`, không theo chuỗi `detail`.

```json
{
  "title": "VALIDATION_ERROR",
  "status": 400,
  "detail": "Check your input.",
  "code": "VALIDATION_ERROR",
  "traceId": "...",
  "retryable": false,
  "errors": { "dateTo": ["dateTo must be on or after dateFrom."] }
}
```

Các status thường gặp:

| Status | Diễn giải client |
|---|---|
| `400` | JSON/query/header không hợp lệ; sửa input. |
| `401` | Chưa đăng nhập, token hết hạn hoặc không hợp lệ. |
| `403` | Đã đăng nhập nhưng không đúng quyền. |
| `404` | Resource không tồn tại hoặc ngoài phạm vi được phép. |
| `409` | Xung đột state/version/idempotency; đọc lại resource, không tự tạo mutation trùng. |
| `422` | Payload đúng shape nhưng vi phạm rule nghiệp vụ. |
| `429`, `5xx` | Tạm thời; chỉ retry theo hướng dẫn `retryable`/`Retry-After`, giữ lại idempotency key. |

## 4. Catalog công khai — API mà frontend đang dùng

### `GET /api/v1/events`

Trả Event đã `Published`, phân trang ổn định. Event draft/cancelled/completed không được public.

| Query | Kiểu | Mô tả |
|---|---|---|
| `q` | string | Tìm theo tên, mô tả, organizer hoặc venue. |
| `category` | string | Lọc category. |
| `location` | string | Lọc theo thành phố/tên/địa chỉ venue. |
| `dateFrom`, `dateTo` | `YYYY-MM-DD` | Khoảng ngày session; `dateTo` phải không sớm hơn `dateFrom`. |
| `status` | string | Public catalog chỉ trả Published; status khác trả trang rỗng. |
| `sortBy` | string | Thứ tự do API hỗ trợ; kiểm tra Swagger trước khi dùng thêm giá trị. |
| `page` | integer, mặc định 1 | Trang hiện tại. |
| `limit` | integer, mặc định 12, tối đa 100 | Kích thước trang. |

Ví dụ response rút gọn:

```json
{
  "events": [
    {
      "id": "uuid",
      "name": "Tên sự kiện",
      "slug": "ten-su-kien",
      "description": "...",
      "category": "Music",
      "status": "Published",
      "imageUrl": "https://...",
      "organizer": { "id": "uuid", "name": "...", "slug": "..." },
      "venue": { "name": "...", "city": "Ho Chi Minh City", "countryCode": "VN", "timeZone": "Asia/Ho_Chi_Minh" },
      "sessions": [{ "id": "uuid", "startsAtUtc": "...Z", "endsAtUtc": "...Z", "ticketTypes": [] }]
    }
  ],
  "total": 41,
  "page": 1,
  "limit": 12,
  "totalPages": 4
}
```

### `GET /api/v1/events/{identifier}`

Đọc chi tiết một Event public theo slug hoặc UUID. Không tìm thấy trả `404 application/problem+json`.

### Availability công khai

| Method | Route | Dùng để |
|---|---|---|
| GET | `/api/v1/event-sessions/{sessionId}/ticket-types` | Đọc loại vé của session. |
| GET | `/api/v1/event-sessions/{sessionId}/availability` | Đọc sức chứa/khả dụng theo session. |
| GET | `/api/v1/event-sessions/{sessionId}/seats` | Đọc sơ đồ/trạng thái ghế. |
| GET | `/api/v1/ticket-types/{ticketTypeId}/availability` | Đọc tồn kho của một loại vé. |

### Mapping thực tế trên frontend

`tixflow-frontend/lib/mock-api/events.ts` lấy `/api/v1/events?page=1&limit=100`, sau đó `tixflow-frontend/lib/catalog.ts` ánh xạ:

| API | Model hiển thị frontend |
|---|---|
| `id`, `slug` | Giữ nguyên `Event.id`, `Event.slug`. |
| `name` | `Event.title`. |
| `sessions[0].ticketTypes[].price` | `Event.ticketTypes[].price`. |
| `sessions[0].ticketTypes[].currency` | `Event.currency`/`ticketTypes[].currency`. |
| `status: "Published"` | `status: "published"`. |
| UTC time | Hiển thị theo `Asia/Ho_Chi_Minh`/timezone của venue. |

Không có `NEXT_PUBLIC_API_URL` thì frontend mới dùng mock data. Khi API URL đã cấu hình, lỗi mạng/5xx được ném lên route error boundary; không âm thầm thay dữ liệu production bằng mock data.

## 5. Luồng nghiệp vụ chính

### 5.1 Giữ vé → checkout → thanh toán → ticket

1. Đọc Event/session/ticket type/availability.
2. Customer gọi `POST /api/v1/holds` với `Idempotency-Key`.
3. Khi Hold hợp lệ, gọi `POST /api/v1/holds/{holdId}/checkout` với `Idempotency-Key` và `X-Expected-Version`.
4. Gọi `POST /api/v1/orders/{orderId}/payments/mock` để tạo mock payment; callback mock được gọi bằng service credential, không phải token người dùng.
5. Đọc `GET /api/v1/orders/{orderId}`, `GET /api/v1/payments/{paymentId}` và sau khi paid, `GET /api/v1/tickets`.

Body tạo Hold:

```json
{
  "eventSessionId": "uuid",
  "items": [
    {
      "ticketTypeId": "uuid",
      "inventoryMode": "GeneralAdmission",
      "quantity": 2,
      "seatIds": []
    }
  ],
  "quote": {
    "totalAmount": 500000,
    "currency": "VND",
    "holdDurationMinutes": 10
  }
}
```

`seatIds` chỉ dùng cho `ReservedSeating`. Giá/quota/state luôn được server kiểm tra lại; quote client không phải bằng chứng cuối cùng.

### 5.2 Check-in

Organizer/Admin gọi một trong `POST /api/v1/tickets/check-ins` hoặc alias `POST /api/v1/check-ins`, kèm `Idempotency-Key`.

```json
{ "eventSessionId": "uuid", "qrToken": "tf1_..." }
```

Hoặc check-in thủ công:

```json
{
  "eventSessionId": "uuid",
  "ticketCode": "TF-EXAMPLE",
  "reason": "Đối chiếu giấy tờ tại cổng"
}
```

Manual check-in luôn cần `reason`; Admin quét QR cũng cần `reason` để audit.

## 6. Danh mục endpoint đang có

Danh sách dưới đây tóm tắt **71 operations** hiện được Swagger công bố. Các mutation management đều yêu cầu quyền phù hợp; xem Swagger để biết body/schema chi tiết.

### System, user và assistant

| Method | Route | Mục đích |
|---|---|---|
| GET | `/health`, `/health/ready`, `/health/live` | Liveness/readiness của service. |
| GET | `/api/v1/system/info` | Tên/version/environment/UTC server. |
| GET | `/api/v1/bookings/status` | Trạng thái module booking. |
| GET | `/api/v1/assistant/status` | Trạng thái AI assistant. |
| GET | `/api/v1/users/me` | Local profile người dùng hiện tại. |
| GET | `/api/v1/users/access/{admin|customer|organizer}` | Probe quyền theo role. |
| POST | `/api/v1/assistant/messages` | Gửi message tới assistant. |
| POST | `/api/v1/assistant/prepare-hold` | Chuẩn bị đề xuất/hold qua assistant. |
| POST | `/api/v1/assistant/confirm-hold` | Xác nhận hold do assistant chuẩn bị. |

### Public catalog

| Method | Route |
|---|---|
| GET | `/api/v1/events` |
| GET | `/api/v1/events/{identifier}` |
| GET | `/api/v1/event-sessions/{sessionId}/ticket-types` |
| GET | `/api/v1/event-sessions/{sessionId}/availability` |
| GET | `/api/v1/event-sessions/{sessionId}/seats` |
| GET | `/api/v1/ticket-types/{ticketTypeId}/availability` |

### Booking, order, payment và ticket

| Method | Route |
|---|---|
| GET, POST | `/api/v1/holds` |
| GET, DELETE | `/api/v1/holds/{holdId}` |
| POST | `/api/v1/holds/{holdId}/cancellations` |
| POST | `/api/v1/holds/{holdId}/checkout` |
| GET | `/api/v1/orders` |
| GET | `/api/v1/orders/{orderId}` |
| POST | `/api/v1/orders/{orderId}/payments/mock` |
| GET | `/api/v1/payments/{paymentId}` |
| POST | `/api/v1/payment-webhooks/mock` |
| GET | `/api/v1/tickets` |
| GET | `/api/v1/tickets/{ticketId}` |
| GET | `/api/v1/tickets/{ticketId}/qr` |
| POST | `/api/v1/tickets/check-ins` và `/api/v1/check-ins` |

### Management: organizer, venue, event, session

| Method | Route |
|---|---|
| GET, POST | `/api/v1/management/organizers` |
| GET, PUT, DELETE | `/api/v1/management/organizers/{organizerId}` |
| GET, POST, PUT | `/api/v1/management/organizers/me` |
| GET, POST | `/api/v1/management/venues` |
| GET, PUT, DELETE | `/api/v1/management/venues/{venueId}` |
| GET, POST | `/api/v1/management/events` |
| GET, PATCH, DELETE | `/api/v1/management/events/{eventId}` |
| POST | `/api/v1/management/events/{eventId}/publish` |
| POST | `/api/v1/management/events/{eventId}/cancellations` |
| POST | `/api/v1/management/events/{eventId}/complete` |
| GET, POST | `/api/v1/management/events/{eventId}/sessions` |
| GET, PUT, DELETE | `/api/v1/management/event-sessions/{sessionId}` |
| GET, POST | `/api/v1/management/event-sessions/{sessionId}/ticket-types` |

### Management: ticket type và seat

| Method | Route |
|---|---|
| GET, PATCH, DELETE | `/api/v1/management/ticket-types/{ticketTypeId}` |
| PUT | `/api/v1/management/ticket-types/{ticketTypeId}/capacity` |
| GET, PUT | `/api/v1/management/ticket-types/{ticketTypeId}/seat-map` |
| POST | `/api/v1/management/ticket-types/{ticketTypeId}/seats` |
| GET, PUT, DELETE | `/api/v1/management/seats/{seatId}` |
| GET | `/api/v1/operations/metrics` |

## 7. Checklist cho người tích hợp

- [ ] Đặt `NEXT_PUBLIC_API_URL` và kiểm tra `GET /health` trước.
- [ ] Luôn dùng Swagger runtime để sinh/update client types.
- [ ] Gửi Bearer token ở route private; không lưu token trong source hoặc log.
- [ ] Gửi `Idempotency-Key` cho mọi mutation yêu cầu; reuse key khi retry cùng request.
- [ ] Đọc lại resource khi gặp `409 VERSION_CONFLICT` hoặc trạng thái thay đổi.
- [ ] Xử lý `application/problem+json` theo `code`/`errors`, không parse thông điệp cho người dùng.
- [ ] Không coi mock payment endpoint là tích hợp cổng thanh toán thật.
- [ ] Chạy lại smoke contract sau khi backend/frontend thay đổi: health, OpenAPI, list Event, empty list, 404 và validation 400.

