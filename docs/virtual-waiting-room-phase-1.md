# Virtual Waiting Room — hợp đồng và đường đo gốc

Ngày chốt bản đầu: 22/11/2026. Tài liệu này là **hợp đồng mục tiêu cho toàn bộ Waiting Room**. Bước 5 đã có test đa replica/restart; [bước 6](virtual-waiting-room-phase-6.md) đã đo ba cặp Disabled/Active và xuất OpenAPI runtime. Cổng tính đúng đạt nhưng ngưỡng độ trễ chưa đạt. API quản trị policy và audit chưa triển khai.

## 1. Phạm vi và ranh giới

- Đơn vị mở bán (`saleId`) ở bản đầu là `eventSessionId`. Một Hold hiện chỉ thuộc một suất diễn; tất cả loại vé của suất đó dùng cùng hàng chờ nếu policy được bật.
- Waiting Room điều tiết quyền **thử tạo Hold**, không đặt trước tồn kho và không bảo đảm khách được vé. PostgreSQL và logic Booking hiện tại vẫn quyết định giá, trạng thái mở bán, tồn kho và ghế.
- Gate nằm ngay trước `POST /api/v1/holds`, sau xác thực Customer. Nó áp dụng cho frontend, client gọi API trực tiếp và lời gọi nội bộ từ Assistant. `GET` catalog/availability, `GET /holds/{id}`, checkout/payment của Hold đã tạo và check-in không đi qua Gate.
- Policy theo suất diễn được lưu bền vững trong PostgreSQL, mặc định `Disabled`. Các trạng thái vận hành là `Disabled`, `Active`, `Paused`, `Closed`. `Paused` dừng cấp lượt **nhưng vẫn giữ Gate**; không dùng nút tắt policy để xử lý quá tải khi đợt mở bán đang diễn ra.
- Redis lưu hàng chờ và grant dùng chung giữa mọi API replica. Redis không là nguồn sự thật về số vé. Khi Redis hoặc controller không sẵn sàng, Gate từ chối Hold mới của sale `Active`/`Paused` (fail closed) với `503 WAITING_ROOM_UNAVAILABLE` và `Retry-After`; không đi vòng qua Redis để tạo Hold.

## 2. Hợp đồng HTTP dự kiến

Tất cả route dưới đây yêu cầu Bearer token với role `Customer`. Response lỗi theo `application/problem+json`, có `code`, `traceId` và `retryable` khi phù hợp. `queueId` chỉ là định danh; server luôn xác minh `sub` trong JWT.

| Method và route | Request | Response chính |
| --- | --- | --- |
| `POST /api/v1/waiting-room/join` | `{ "eventSessionId": "uuid" }` | `200 {"data":{"mode":"Direct"}}` khi policy `Disabled`; hoặc `200/201 {"data":{"mode":"Waiting","queueId":"uuid","status":"Waiting","positionEstimate":42,"pollAfterMs":3000}}`. Gọi lại cùng user/sale trả cùng entry đang hoạt động và cùng thứ tự. |
| `GET /api/v1/waiting-room/status?queueId={id}` | Không có body | `200 {"data":{"status":"Waiting|Eligible|Admitted|Consumed|Abandoned|Expired|Rejected","positionEstimate":41,"pollAfterMs":3000,"admissionToken":null}}`. Khi `Admitted`, trả token trong `data.admissionToken` cho đúng chủ entry; `Cache-Control: no-store`. |
| `DELETE /api/v1/waiting-room/{queueId}` | Không có body | `204`; đánh dấu `Abandoned` nếu chưa tiêu thụ, gọi lặp vẫn an toàn. |
| `PUT /api/v1/management/event-sessions/{sessionId}/waiting-room` | Policy và hạn mức cấp lượt | Organizer sở hữu Event hoặc Admin cấu hình trước đợt bán; thay đổi được audit. `Paused` giữ Gate. Route này cần bổ sung vào OpenAPI khi triển khai. |
| `POST /api/v1/holds` | Body và `Idempotency-Key` hiện có; thêm `X-Admission-Token` **chỉ khi policy Active/Paused** | Gate xác thực token và ràng buộc với một canonical Hold intent trước khi chạy transaction Booking. Response thành công/lỗi tồn kho của Booking giữ hợp đồng hiện có. |

`positionEstimate` là số **ước lượng**, có thể đổi khi người chờ bỏ hàng hoặc hết hạn; UI không hứa thời điểm vào chính xác. Server trả `pollAfterMs` và giới hạn tần suất join/status để tránh polling storm. Mặc định ban đầu: polling 3–5 giây có jitter phía client, entry hết hạn sau 5 phút không thấy hoạt động, grant cho lần tạo Hold đầu có TTL 2 phút. Các giá trị này là cấu hình được ghi trong manifest tải, không điều chỉnh âm thầm trong một phép so sánh.

## 3. Trạng thái và tính đúng

```text
New → Waiting → Eligible → Admitted → Consumed
        ├──────────────→ Abandoned
        ├──────────────→ Expired
        └──────────────→ Rejected
```

- `New`/`Eligible` có thể là trạng thái nội bộ; API chỉ công bố trạng thái có ý nghĩa cho client. Mỗi `(sub, saleId)` có tối đa một entry hoạt động. Reconnect hoặc tab thứ hai phải nhận lại entry đó, không được nhảy lên đầu hàng.
- Sequence được cấp nguyên tử theo sale. Controller cấp grant theo thứ tự các entry còn hiệu lực; nhiều replica hoặc controller restart không được cấp quá budget của cùng một cửa sổ.
- Token được ký, gắn với `sub`, `saleId`, `queueId`, `grantId`, `nonce`, `issuedAt` và `expiresAt`. Không lưu token trong URL, log hoặc analytics; phía client giữ trong bộ nhớ và có thể đọc lại qua status bằng Bearer token khi tải lại trang.
- Lần tạo Hold đầu ràng buộc `grantId` với `Idempotency-Key` và hash của **canonical request** theo đúng cách Booking hiện tính. Cùng token, key và payload được retry; dùng token cho key/payload khác bị từ chối. Cần xử lý riêng trường hợp response Hold đã commit nhưng client mất kết nối: đọc/replay kết quả idempotency đã commit, không tạo mutation thứ hai. Nếu grant hết hạn trước lần tạo Hold đầu, khách phải vào hàng lại.
- `Consumed` nghĩa là grant đã gắn với một intent, **không** có nghĩa vé đã được giữ thành công. `Admitted` cũng không bảo đảm còn vé: Booking có thể trả `409 INSUFFICIENT_CAPACITY` hoặc `SEAT_UNAVAILABLE`.
- Khi Assistant dùng `confirm-hold`, nó phải gửi admission token tới cùng Gate. Vì confirmation token của Assistant hiện có TTL 2 phút, UI nên xếp hàng trước khi chuẩn bị/xác nhận proposal; không giữ proposal cũ trong thời gian chờ dài.

## 4. Mã lỗi và hành vi client

| HTTP/code | Ý nghĩa | Hành vi client |
| --- | --- | --- |
| `401`/`403` | Chưa xác thực hoặc sai role/owner | Đăng nhập hoặc dừng thao tác. |
| `409 WAITING_ROOM_REQUIRED` | Sale có Gate, Hold thiếu grant | Mở lại trạng thái hàng chờ. |
| `409 ADMISSION_INVALID` / `ADMISSION_EXPIRED` / `ADMISSION_INTENT_CONFLICT` | Token sai user/sale, hết hạn hoặc tái dùng cho intent khác | Không retry mutation mới với token cũ; kiểm tra status hoặc vào hàng lại. |
| `409 INSUFFICIENT_CAPACITY` / `SEAT_UNAVAILABLE` | Đã qua Gate nhưng hết vé/ghế | Hiển thị hết vé, không gọi đó là lỗi hàng chờ. |
| `429 RATE_LIMITED` | Polling hoặc request vượt rate limit | Tôn trọng `Retry-After`; không cộng vào số Hold thành công. |
| `503 WAITING_ROOM_UNAVAILABLE` | Redis/controller không khả dụng cho sale có Gate | Giữ UI ở trạng thái chờ, tôn trọng `Retry-After`; không gửi Hold không token. |

Tên/status cuối cùng phải được kiểm tra trong OpenAPI và integration test trước khi frontend tích hợp. Không đổi mã lỗi Booking hiện có.

## 5. Đường đo trước/sau

### Nguồn đã có, chưa phải phép đo mới

- Baseline mới trên đúng commit `e55966b` và image ứng viên sạch đã được đo ngày 22/11/2026: ba smoke và ba spike đều đạt. Bảng số liệu và giới hạn nằm tại `docs/reports/waiting-room-baseline-20261122.md`. Đây là phép đo **trước Waiting Room**, không phải kết quả admission.
- `docs/release/measured-results.md` tổng hợp chiến dịch 22/10/2026: mỗi variant có ba run smoke, ramp, spike hợp lệ. Spike 100 VU: Hold optimized median p95 **3.797,42 ms**, p99 **5.430,99 ms**; callback optimized p95 **4.548,13 ms**. Các số này là dấu hiệu cần đo lại, không phải SLO của bản hiện tại.
- Soak 10 VU/30 phút, baseline-1 dừng với `Load database SQL failed.`; chưa có run soak hợp lệ. Nguyên nhân PostgreSQL dừng chưa được xác định. Không dùng chiến dịch đó để tuyên bố khả năng chịu tải dài hạn.
- `load/scenarios.js` hiện là closed-loop k6, gọi thẳng Hold, tắt limiter và yêu cầu `admission_rejections == 0`. Nó không đo thời gian chờ hay tốc độ đến độc lập. Không dùng nguyên profile này để tuyên bố Waiting Room đạt tải đến cố định.

### Protocol cần lưu trước khi triển khai Gate

1. Ghi commit/image ID, manifest hash, dataset, số replica, CPU/RAM/pool, load generator, profile và thời gian. Không so sánh các run khác cấu hình hoặc thay source giữa run.
2. Chạy lại smoke và spike **không Waiting Room** với image ứng viên hiện tại; tối thiểu ba run độc lập mỗi profile nếu dùng làm baseline so sánh. Soak cần điều tra lỗi và chạy đạt riêng trước khi kết luận độ bền.
3. Protocol Waiting Room mới dùng offered load có thời điểm đến được ghi rõ, thực hiện `join → status → grant → hold → checkout → paid → ticket`. Giữ cùng dataset và tài nguyên giữa `Disabled` và `Active`. Tính thêm `join→terminal decision`, thời gian chờ, grants/s, admission/rejection, abandoned/expired và backlog/drain. Báo cả tỷ lệ đạt Hold/Order Paid/Ticket, không chỉ p95 của những request được cấp lượt.
4. Cổng tính đúng cho mọi run: `oversold = 0`, không ghế/hold/order/payment/ticket trùng, không thiếu vé sau Paid, không bỏ qua Gate, retry cùng intent không tạo Hold thứ hai. Lỗi kỹ thuật <1% theo protocol hiện có; conflict hết vé, 429 rate limit và từ chối admission phải báo riêng.
5. Trước vòng validation, ghi ngưỡng local theo workload cho p95/p99 Hold **sau Gate**, thời gian `join→terminal decision`, queue drain và Paid→ticket. Số liệu dùng để chọn ngưỡng và số liệu validation là hai tập run khác nhau; không gọi ngưỡng local là SLA production.

### Ngưỡng local khóa trước khi viết benchmark Waiting Room

Các ngưỡng dưới đây là mục tiêu cho **protocol mới**: 100 Customer khác nhau bắt đầu join trong một giây, sale thử nghiệm có 50 vé, một API 2 CPU/1 GiB, PostgreSQL 1 CPU/512 MiB, cùng dataset và máy phát tải ở cả cấu hình Gate off/on. Đây không phải khẳng định hệ thống đã đạt. Protocol phải báo cả khách bị hết vé, bỏ hàng và không được grant, thay vì chỉ chọn người mua thành công.

| Chỉ tiêu | Ngưỡng validation local |
| --- | --- |
| Sai lệch tồn kho/ghế, Hold/Order/Ticket trùng, bypass Gate | **0** ở mọi run. |
| Lỗi kỹ thuật của API và Waiting Room | **<1%** trên toàn bộ request đã phân loại; không tính hết vé là lỗi kỹ thuật. |
| `GET status` p95 | **≤500 ms** khi 100 khách đang chờ. |
| `POST holds` p95/p99 sau khi được cấp lượt | **≤3.000/6.000 ms** trên ba run độc lập; phải báo song song tỷ lệ Hold thành công. |
| `join→Admitted hoặc biết đã hết vé` p95 | **≤30 giây** cho khách còn hoạt động; báo riêng số khách chưa có quyết định ở mốc 30 giây. |
| Hàng chờ sau khi dừng cấp lượt / Redis mất kết nối | Không phát grant mới khi `Paused` hoặc không đủ tín hiệu an toàn; khôi phục có kiểm soát và ghi thời gian drain. |

Nếu benchmark mới cho thấy ngưỡng không phù hợp, ghi lại thay đổi và chạy **tập validation khác**; không sửa ngưỡng sau khi nhìn cùng run rồi gọi là pass. Độ trễ từ join đến vé phát hành và Paid→ticket phải được đo, nhưng chưa đặt ngưỡng số trước khi có protocol và dữ liệu đầy đủ cho Worker.

## 6. Thứ tự triển khai tiếp theo

1. Redis queue + atomic join/rejoin/status/abandon/grant và policy theo session (đã triển khai ở bước 2; chưa bật policy qua API).
2. Gate `POST /holds` và replay idempotency; tích hợp Assistant (đã triển khai ở bước 3).
3. UI phòng chờ, xử lý reconnect/expiry/sold out (đã triển khai ở bước 4 cho checkout và Assistant).
4. Test đồng thời, đa replica, Redis/controller restart; mở rộng load stack thêm Redis và profile k6 riêng (đã triển khai ở bước 5; crash/soak dài hạn chưa đo).
5. Đo bật/tắt, phân tích SLO và xuất OpenAPI/runtime Swagger (đã thực hiện ở bước 6; ngưỡng status/Hold chưa đạt nên chưa điều chỉnh controller khi chưa tách rõ điểm nghẽn).

Điểm chạm mã nguồn hiện tại: `src/TixFlow.Api/Modules/Booking/BookingModule.cs`, `src/TixFlow.Api/Modules/Assistant/AssistantBookingApi.cs`, `src/TixFlow.Api/Performance/TrafficLimits.cs`, `src/TixFlow.Infrastructure/DependencyInjection.cs`, frontend `components/checkout/checkout-view.tsx` và `app/assistant/page.tsx`.
