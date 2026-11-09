# APP-01–06 — kết quả kiểm tra 03/11/2026

Evidence: `artifacts/acceptance/20261103-app01-06/`. Người kiểm tra: Codex. Frontend source `3485448c3c2ae7a3af48ffbc55197dd5153c17d2`; backend source `583b40e11e6de00cf58b77760db129ff14d34168` với thay đổi lockfile có sẵn, ghi trong status. Backend container đang chạy từ trước; chưa khẳng định image trùng source hiện tại.

Phạm vi thực hiện: HTTP local thật; browser guest trên localhost:3000 (desktop mặc định và mobile 390×844); thử lỗi cách ly ở adapter TypeScript thật bằng VM + fetch giả lập. Không tắt API, không tạo giao dịch, không đăng nhập tài khoản thật. Thử adapter không thay thế toàn bộ kiểm thử UI có đăng nhập. Chưa sửa mã nghiệp vụ.

| Case | Kết quả | Actual và bằng chứng | Còn thiếu / cần sửa |
|---|---|---|---|
| APP-01 | PASS — smoke môi trường hiện hành | Health, frontend, discovery, OpenAPI 200. Browser guest qua check-sso/login_required rồi về trang chủ. DB pg_isready OK; Redis PONG; RabbitMQ ping OK. Worker running, mẫu 13 dòng log có 0 dòng lỗi. `http-results.json`, `services.log`, `containers.jsonl`, `worker-smoke.json`. | Đây là smoke; không chứng minh Worker đã phát vé hay image khớp source. |
| APP-02 | PARTIAL | CORS preflight 204, allow-origin `http://localhost:3000`, allow-header authorization. Trang chủ/catalog/404 guest không thấy CORS hoặc mixed-content error trong console đã xem; có cảnh báo Next scroll-behavior. | Chưa thu request từ các màn hình cần login. Catalog gọi API ở server nên console browser sạch không đủ chứng minh request API từ browser đúng ở mọi màn hình. |
| APP-03 | FAIL | JSON catalog đúng envelope `events,total,page,limit,totalPages`. Sample giữ đúng ID/slug/title, Published→published, 650000 VND. API empty totalPages=0; adapter FE tự tạo totalPages>=1. OpenAPI live chỉ có response 200 description OK, không có schema body hoặc lỗi 400/404 cho catalog/detail. Khoảng ngày đảo trả 400 ProblemDetails có errors.dateTo; không phải 422. `contract.json`, response bodies. | Bổ sung schema/response lỗi OpenAPI; đồng bộ paging/filter với API. FE tải cố định page=1&limit=100 rồi xử lý fixture/client-side. useApi chỉ đọc detail nên không trình bày errors theo field. |
| APP-04 | FAIL | Adapter thật trả 41 fixture khi fetch lỗi, kể cả 404; getEventBySlug trả fixture tương ứng. `adapter-faults.json`. | Loại fallback nghiệp vụ; còn thiếu Network capture đã lọc secret ở từng màn hình. Không coi adapter request trace là HAR/browser Network. |
| APP-05 | FAIL | Inject 401/403/404/409/422/500, timeout và offline: catalog vẫn trả 41 event, detail fixture, không đẩy lỗi cho error boundary/retry. useApi 409 không có detail báo nhầm email conflict. `adapter-faults.json`. | Chạy lại UI sau sửa, gồm timeout thực, bỏ loading, retry, phiên đăng nhập. Timeout ở đây được mô phỏng bằng TimeoutError, chưa đo thời gian timeout thực. Không dừng API đang dùng. |
| APP-06 | FAIL / coverage một phần | Browser desktop/mobile: tìm chuỗi không tồn tại hiện No matching events found; Show All Events khôi phục danh sách trên mobile. Deep-link event không tồn tại hiện 404; refresh đi qua check-sso rồi trở lại đúng 404. Tuy nhiên API trả empty 200 bị adapter biến thành 41 fixture. PNG desktop/mobile empty/not-found trong evidence. | Error/retry khi API hỏng không đạt vì fallback. Chưa kiểm tra đủ loading thoáng qua, nút back và phục hồi giao dịch trên server sau refresh/relogin. |

## Cách tái hiện lỗi cách ly

Chạy từ frontend: `node scripts/check-smoke-contract.cjs ../tixflow-backend/artifacts/acceptance/<run-id>` sau khi thu response OpenAPI/catalog vào thư mục mới. Script transpile và chạy các module hiện tại (`lib/mock-api/events.ts`, `lib/catalog.ts`, `lib/api.ts`) với fetch stub, không sửa ứng dụng. Trace chỉ lưu URL/outcome, không lưu Authorization.

Các mode lỗi đã thử: 401,403,404,409,422,500,timeout,offline; thêm empty 200. Cả chín mode đều trả listTotal=41. Detail với slug fixture cũng trả fixture. Empty response trong mode này áp dụng cho cả list/detail, nên kết luận empty-state chính dựa vào list.

## Ưu tiên xử lý

1. Catalog sử dụng dữ liệu server, phân biệt empty/404/network error; không tự thêm fixture.
2. Bổ sung typed response/schema và status code thực cho OpenAPI; truyền filter/paging đúng contract.
3. Sửa mapping lỗi 409 và validation theo field, timeout/retry.
4. Kiểm thử browser có tài khoản Customer/Organizer test, lưu Network đã lọc secret, kiểm tra state giao dịch sau back/refresh/deep-link. Sau đó mới tick APP-02–06.

Không thay đổi kết quả phần 0 ngày 02/11; đây là lượt kiểm tra mới của phần 1. Có file report body ProblemDetails ban đầu dạng byte-per-line do PowerShell; bản UTF-8 đọc được được lưu riêng với hậu tố `.decoded.json`, bản gốc được giữ nguyên.
