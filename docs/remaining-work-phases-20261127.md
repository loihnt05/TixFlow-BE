# Các phase còn lại của TixFlow

Đối chiếu ngày 27/11/2026. Tài liệu nguồn: [checklist nghiệm thu](checklist-nghiem-thu-va-trien-khai.md), `CHECKLIST-KE-HOACH-TIXFLOW.md` ở workspace cha, [ghi chú Waiting Room](virtual-waiting-room-tixflow.md) và [báo cáo bước 9](virtual-waiting-room-phase-9.md). Các checklist cũ mô tả trạng thái tại ngày lập; dấu `[x]` hoặc tỷ lệ phần trăm không thay cho kết quả chạy trên source hiện tại. Chỉ chuyển phase khi có kết quả thực tế và evidence của phase đó.

| Thứ tự | Phase nhỏ | Đầu việc còn phải đóng | Điều kiện chuyển bước | Trạng thái |
| --- | --- | --- | --- | --- |
| 01 | Hợp đồng API–frontend | Chốt lọc/paging catalog; chuẩn hóa ProblemDetails, retry và trạng thái lỗi UI (`APP-02..06`, `APP-07/08`) | Typecheck/build frontend; test response 400/401/403/404/409/422/5xx; ghi rõ filter client/server | Mã/build đạt, chờ browser |
| 02 | Giao diện quản trị | Đối chiếu Orders, Attendees, Analytics, check-in, tiền tệ/ngày giờ (`APP-21..28`) | Mọi số liệu đọc DB; check-in lặp có thông báo và refresh giữ trạng thái | Sửa UI 02A đã build, chờ E2E |
| 03 | Tài khoản và dữ liệu nghiệm thu | Chuẩn bị 2 Customer, 2 Organizer, Admin; seed demo có version, ngày còn hiệu lực (`AUTH-01..04`, `DATA-01..11`) | Tái tạo fixture, không trùng ID, đúng quyền sở hữu; lưu manifest không chứa secret | Script refresh 03A đã viết, chờ DB |
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

### 02A — Check-in và lọc theo sự kiện

- [x] Khi đổi sự kiện trong Organizer, chỉ giữ `manualSessionId` nếu session đó vẫn thuộc sự kiện đang chọn; nếu không, chọn session đầu hợp lệ. Không gửi check-in nhầm sang session của sự kiện trước.
- [x] Sau check-in thành công, nếu tải lại danh sách lỗi, UI báo rõ check-in đã lưu và hướng dẫn làm mới; không hiển thị như thao tác check-in thất bại.
- [x] `npm run lint` và `npm run build` đạt trên Next 16.4.6.
- [ ] Dùng Organizer thật check-in một vé Issued, quét lặp, đổi bộ lọc sự kiện rồi check-in; refresh trang và đối chiếu API/DB. Đây là cổng để đóng 02A.

### 02B — Số liệu và trang Admin

- [x] Admin check-in cập nhật vé từ response API; nếu tải lại dashboard thất bại sau khi check-in đã lưu, UI không báo nhầm thao tác check-in thất bại.
- [ ] Đối chiếu Orders, Attendees, Analytics với DB cho từng Organizer và Admin; xác nhận ownership, trạng thái hủy và khung ngày Asia/Ho_Chi_Minh.
- [ ] Kiểm tra các màn Admin cần cho demo và ghi rõ phạm vi quên mật khẩu/SMTP theo `APP-25..27`.

## Phase 03A — Làm mới fixture nghiệm thu

- [x] Script `Invoke-DemoAcceptanceRefresh.ps1` chỉ dùng database `tixflow_acceptance`, yêu cầu anchor UTC và tạo manifest SHA-256 sau khi SQL thành công.
- [x] SQL v2 giữ ID fixture, chỉ cập nhật cửa sổ bán và thời gian hai session; cùng anchor có thể chạy lại, đổi anchor hoặc refresh lần đầu sau khi đã có Hold thì từ chối.
- [ ] Chạy trên database nghiệm thu sạch; kiểm tra seed lần hai không nhân đôi hoặc đổi `updated_at_utc`; thử sai DB, đổi anchor và fixture đã có Hold đều bị từ chối. Lưu manifest và số row thực tế.

## Phase 03B — Tài khoản và dữ liệu có quyền sở hữu

- [x] Gắn metadata fixture vào các ca AI có ID/ngày cố định; `Evaluate-Phase11.py --preflight-only` đọc catalog công khai, xác nhận event/session/ticket type còn hợp lệ trước khi đánh giá provider. Ca hết hạn hoặc thiếu dữ liệu dừng sớm; chưa tuyên bố DATA-09 đạt khi chưa chạy với DB thật.
- [ ] Chuẩn bị 2 Customer, 2 Organizer và 1 Admin trên Keycloak test; đăng nhập để đồng bộ `sub` và role, không tạo user giả trực tiếp trong DB.
- [ ] Tạo dữ liệu Hold/Order/Payment/Ticket qua API thật, đối chiếu owner và inventory; đồng bộ ngày/case của bộ đánh giá AI.
