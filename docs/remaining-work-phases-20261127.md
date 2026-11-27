# Các phase còn lại của TixFlow

Đối chiếu ngày 27/11/2026. Tài liệu nguồn: [checklist nghiệm thu](checklist-nghiem-thu-va-trien-khai.md), `CHECKLIST-KE-HOACH-TIXFLOW.md` ở workspace cha, [ghi chú Waiting Room](virtual-waiting-room-tixflow.md) và [báo cáo bước 9](virtual-waiting-room-phase-9.md). Các checklist cũ mô tả trạng thái tại ngày lập; dấu `[x]` hoặc tỷ lệ phần trăm không thay cho kết quả chạy trên source hiện tại. Chỉ chuyển phase khi có kết quả thực tế và evidence của phase đó.

| Thứ tự | Phase nhỏ | Đầu việc còn phải đóng | Điều kiện chuyển bước | Trạng thái |
| --- | --- | --- | --- | --- |
| 01 | Hợp đồng API–frontend | Chốt lọc/paging catalog; chuẩn hóa ProblemDetails, retry và trạng thái lỗi UI (`APP-02..06`, `APP-07/08`) | Typecheck/build frontend; test response 400/401/403/404/409/422/5xx; ghi rõ filter client/server | Mã/build đạt, chờ browser |
| 02 | Giao diện quản trị | Đối chiếu Orders, Attendees, Analytics, check-in, tiền tệ/ngày giờ (`APP-21..28`) | Mọi số liệu đọc DB; check-in lặp có thông báo và refresh giữ trạng thái | Mã hiện có, chờ E2E sau 01 |
| 03 | Tài khoản và dữ liệu nghiệm thu | Chuẩn bị 2 Customer, 2 Organizer, Admin; seed demo có version, ngày còn hiệu lực (`AUTH-01..04`, `DATA-01..11`) | Tái tạo fixture, không trùng ID, đúng quyền sở hữu; lưu manifest không chứa secret | Chờ 02 và môi trường |
| 04 | E2E xác thực | Login, refresh, logout, quyền và ownership (`AUTH-05..15`) | Ma trận browser/API có Actual và evidence, không rò dữ liệu giữa user | Chờ 03 |
| 05 | E2E mua vé | Catalog → Hold → payment → QR → check-in, race/expiry/restart (`APP-07..20`) | UI, API và DB khớp; `oversold=0`; có evidence payment fail và tranh vé | Chờ 04 |
| 06 | Waiting Room capacity | Giới hạn grant đồng thời theo Redis; migration và quản trị policy | Test Redis/PostgreSQL đa replica và fail closed chạy qua | Mã đã viết, chờ test runtime |
| 07 | Waiting Room active session | Giữ slot từ Hold qua checkout, heartbeat/timeout, giải phóng đúng một lần | `ActiveUsers ≤ Capacity` dưới tranh chấp/restart; không rò slot | Chờ 06 |
| 08 | Admission thích ứng | Budget theo latency/lỗi/tải, hysteresis và metric quyết định; kiểm tra chống bot/bypass | Fault injection giảm/dừng grant và phục hồi có kiểm soát | Chờ 07 |
| 09 | Tải và độ tin cậy | Soak 30 phút; đối chứng Active/Disabled 5.000 join, 1.000 admission, 3 replica; Redis/network failover (`PERF-SOAK`, `PERF-WR`) | Raw k6, invariant, SLO đã khóa, báo cáo tái lập; không gọi local benchmark là production | Chờ 08 và Docker |
| 10 | AI provider thật | Seed/case đồng nhất, 20 case, chấm semantic/tool/latency/cost, test hội thoại (`AI-01..20`) | Có provider response thật, bảng chấm thủ công và evidence; key ngoài Git | Chờ 05 và key |
| 11 | VPS và vận hành | DNS/TLS, image digest, smoke, backup/restore toàn stack, rollback, alert (`VPS-01..32`) | HTTPS và E2E chạy trên VPS; RTO/RPO thực đo; operator nhận alert | Chờ 09, 10 và VPS |
| 12 | Bàn giao khóa luận | OpenAPI/snapshot/manifest, báo cáo 6 chương, video 8–12 phút, slide (`FINAL-01..10`) | Mọi số liệu truy về raw evidence/source; demo khớp bản triển khai | Chờ 11 |

## Phase 01 đang thực hiện

- [x] Chốt `/events` tải đủ trang API (`limit=100` mỗi request) rồi lọc/paging ở client cho bộ catalog demo. Bỏ trần 1.000 event vô tình của route và báo lỗi khi response trang đầu sai cấu trúc. Không tuyên bố từng filter UI đã được gửi vào `GET /api/v1/events`. Đường API hỗ trợ `q`, `category`, `location`, `dateFrom`, `dateTo`, `status`, `sortBy`, `page`, `limit` cho client khác. Khi catalog lớn hoặc phép đo thấy tải all-page không đạt, chuyển UI sang lọc/paging server ở một phase tối ưu riêng.
- [x] Sửa mặc định `ApiError.retryable` cho HTTP 5xx; `REQUEST_IN_PROGRESS` 409 vẫn retry được; giá trị `retryable` từ server có quyền ưu tiên.
- [x] Đồng bộ `node_modules` với `package-lock.json`: Next cài đúng 16.4.6. `npm audit fix` cập nhật `postcss-selector-parser` 6.1.4 → 6.1.6 trong lockfile; audit còn 0 cảnh báo. `npm run lint` và production `npm run build` đều đạt trên máy hiện tại.
- [ ] Kiểm tra các trạng thái lỗi và 404 bằng browser ở cả desktop/mobile, với backend thật. Ghi Actual/Evidence theo mẫu trong checklist.

## Cổng môi trường hiện tại

Backend và test project đã build sạch ở lượt trước. Test tích hợp Waiting Room đã bị hủy trước khi chạy vì testhost không kết nối trong 90 giây; kiểm tra Docker CLI cho thấy Docker daemon không hoạt động. Backend localhost:8080 cũng không truy cập được trong sandbox. Các mục cần Redis/PostgreSQL, browser đăng nhập, AI key hoặc VPS vẫn ở trạng thái chưa nghiệm thu cho đến khi có môi trường và evidence tương ứng. Không suy trạng thái PASS từ mã hoặc báo cáo cũ.

## Kiểm kê trước Phase 02

`organizer-view.tsx` hiện gọi trực tiếp các API management `events`, `orders`, `attendees`, `analytics`; check-in gửi `POST /api/v1/check-ins`, xử lý lỗi vé đã dùng và đọc lại dữ liệu sau thành công. Backend có đúng các route đó. Vì vậy các dòng APP-21..24 trong checklist cũ là việc **nghiệm thu bằng dữ liệu thật**, không phải viết lại toàn bộ màn hình. Cần chạy E2E qua tài khoản Organizer và đối chiếu DB trước khi tick.
