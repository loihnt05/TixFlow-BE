# Kiểm kê TixFlow — 27/10/2026

## Kết luận

TixFlow đã có phần lõi backend bán vé, giao diện khách hàng kết nối API, cơ chế đồng thời/worker/QR, bộ thực nghiệm, AI assistant có cơ chế xác nhận, bộ setup VPS và bộ tài liệu bàn giao. Stack development đang chạy local. **Chưa đủ điều kiện chốt release hoặc nghiệm thu production.**

Hai việc nổi bật ở bản hiện tại là frontend không qua cài đặt sạch do lockfile lệch phiên bản, và campaign tải vẫn thất bại ở soak. Một số màn hình Organizer/Admin vẫn là prototype. AI local đang fallback; chưa có kết quả đánh giá provider thật.

Đối chiếu kế hoạch Word người dùng cung cấp, source hai repository, báo cáo/raw/TRX, gói review 25/10 và các kiểm tra mới bên dưới. Đây là kiểm kê triển khai và bằng chứng kiểm thử; không phải kiểm toán bảo mật toàn diện hay nghiệm thu mọi luồng UI.

## Đã triển khai đến đâu

| Phạm vi | Đã có | Phần chưa đóng |
|---|---|---|
| Phase 0–1: nền tảng, thiết kế | API/Worker/PostgreSQL/Keycloak/RabbitMQ/Redis; tài liệu actors, API, business rules, state/sequence, database | Chưa có bằng chứng người khác tái tạo bản hiện tại từ checkout sạch |
| Phase 2: authentication | OIDC/PKCE, JWT audience/roles, profile theo `sub`, phân quyền Customer/Organizer/Admin | Chưa nghiệm thu lại login/refresh/logout qua browser cho các thay đổi frontend mới |
| Phase 3–4: catalog/inventory | API quản trị event, venue, session, loại vé, ghế, capacity; catalog công khai và form event gọi API | Giao diện quản trị chưa bao phủ đầy đủ API; dashboard có dữ liệu mẫu |
| Phase 5: hold | Giữ vé/ghế có TTL, kiểm soát đồng thời bằng PostgreSQL, idempotency, hủy hold và rollback | Cần hoàn tất kiểm thử tải dài và chốt SLO |
| Phase 6: checkout/payment | Hold → order, giá từ server, payment/callback giả lập, xử lý callback lặp và race với expiry | Chưa nghiệm thu lại toàn bộ luồng bằng browser; thanh toán giả lập đúng phạm vi kế hoạch |
| Phase 7: worker/tickets | Expiry, outbox/inbox, retry/DLQ, phát vé QR, API check-in chống lặp; email/notification giả lập | Nút check-in trong Organizer hiện chỉ đổi state phía frontend, chưa gọi API check-in |
| Phase 8–9: baseline/tối ưu | Có raw trước/sau, regression, kiểm tra invariant, cache metadata, metrics, test chia sẻ DB qua hai API host và smoke hai replica sau proxy | Số đo baseline/optimized có phạm vi riêng; không thay cho SLA hoặc khả năng chịu 10.000 VU |
| Phase 10: k6 | Smoke/ramp/spike có 18 run đạt; raw, manifest, monitoring và báo cáo tồn tại | Soak lỗi; campaign chưa hoàn thành; một số p95/p99 optimized xấu hơn baseline |
| Phase 11: AI | UI/API assistant, typed tools, xác nhận trước hold, ràng buộc user/expiry, audit, timeout/fallback; bộ 20 case | Provider thật chưa được đánh giá; API local hiện trả `fallback` với provider OpenAI |
| Phase 12: VPS | Compose production, Nginx/TLS, migration, backup/restore/rollback, monitoring, workflow CI/image/deploy; fixture integration có bằng chứng | Chỉ setup theo yêu cầu; chưa VPS/DNS/HTTPS/restore toàn stack thực tế; chưa chốt bản image publish |
| Phase 13: bàn giao | Bản thảo 6 chương, sơ đồ, OpenAPI 49 path, 11 slide, demo script, offline walkthrough, video bằng chứng 42 giây, ZIP có hash | Còn gate G1–G7; ZIP 25/10 không chứa thay đổi frontend mới; chưa video thao tác ứng dụng 8–12 phút hoặc báo cáo theo mẫu trường |

Thanh toán thật và microservices nằm ngoài phạm vi kế hoạch gốc. Email giả lập cũng là hạng mục được kế hoạch cho phép; không cần thêm cổng thanh toán hoặc SMTP chỉ để đóng phạm vi giả lập này. Nếu muốn vận hành bán vé thật, cần xác định một phạm vi tích hợp riêng.

## Phát hiện cần xử lý

### 1. Frontend đang chặn clean install/CI

`package.json` yêu cầu `puppeteer-core ^25.16.0`, trong khi `package-lock.json` khóa `25.13.0`. `npm ci --dry-run --ignore-scripts --no-audit --no-fund` trả **EUSAGE** và nêu thêm các dependency liên quan không khớp. Dockerfile và workflow production đều chạy `npm ci`, nên cần đồng bộ manifest/lockfile trước khi build bản hiện tại trên máy sạch.

Metadata root của lockfile còn lệch range `lucide-react` và `postcss`; phiên bản đã khóa của hai package này vẫn thỏa range hiện tại. Không quy tất cả các chênh lệch metadata thành lỗi cài đặt.

Lần chạy dry-run dùng Node 24.22.1/npm 11.16.0 của môi trường kiểm kê, có cảnh báo engine vì dự án yêu cầu Node 22. Docker/CI đã khai báo Node 22. Kết luận lockfile lệch được kiểm tra trực tiếp và được npm xác nhận; chưa có clean build Node 22 mới cho bản frontend này. Không cài đặt/nâng cấp dependency trong lần kiểm kê.

### 2. Các phần UI còn dùng dữ liệu mẫu

- `components/organizer/organizer-view.tsx`: events/profile gọi API thật, nhưng Orders và Attendees lấy từ `lib/mock-api/orders.ts` và `attendees.ts`.
- Chỉ số tickets sold/revenue/orders/attendees và biểu đồ analytics còn hard-code. Không dùng chúng làm số liệu kinh doanh thực tế.
- Nút check-in chỉ gọi `setCheckedIn`; CSV xuất từ danh sách attendees mẫu.
- Route attendees theo event vẫn dùng `lib/mock-api/organizers.ts`, có fixture/localStorage.
- `/admin` hiện là trang xác nhận quyền truy cập, chưa có bộ màn hình quản trị nghiệp vụ hoàn chỉnh.
- `/forgot-password` chỉ hiển thị thông báo simulated flow, chưa gửi yêu cầu reset mật khẩu qua Keycloak.
- Articles/blog/partners dùng nội dung fixture. Có thể chấp nhận cho demo nếu ghi rõ phạm vi nội dung tĩnh.

Tên thư mục `mock-api` không đủ để kết luận mọi dữ liệu là giả: `lib/mock-api/events.ts` thực tế gọi API catalog. Account, checkout và tickets cũng có tích hợp API thật.

### 3. Hiệu năng chưa đạt cổng nghiệm thu cuối

Campaign mới nhất tìm thấy: `artifacts/phase10/campaign-20261022T131923523Z/status.json`, trạng thái `failed`, lỗi `Load database SQL failed.` ở soak. Log/báo cáo ghi PostgreSQL không chạy; chưa có kết luận nguyên nhân vì sao dừng.

Smoke/ramp/spike mỗi profile có 6/6 run đạt. Soak có 0/1 run quan sát đạt, chưa đủ 6 run so sánh. Các mức cấu hình trong campaign là smoke 10, ramp 20, spike 100, soak 10 VU; không phải bằng chứng 10.000 VU.

Ví dụ ramp, median p95 giữa các run: hold 811,21 → 1.090,09 ms; callback 715,50 → 1.692,33 ms. Cần đánh giá cả throughput và độ trễ đuôi, không chỉ lấy số đo tốt hơn từ phase 9. Chi tiết tại [measured-results.md](release/measured-results.md).

### 4. Snapshot bàn giao đã cũ đối với frontend

Trong phạm vi so sánh source/tests/deployment/build đã ghi ở JSON evidence: backend 144 file khớp gói 25/10, không thêm/xóa; frontend 17 file đổi, 19 file thêm và 1 file bỏ (bao gồm chuyển `app/page.tsx` sang `app/(home)/page.tsx`). Có thêm loading/error/not-found states và thay đổi authentication/Organizer.

ZIP 25/10 vẫn nguyên vẹn, nhưng không đại diện frontend hiện tại. Sau khi hoàn tất sửa và kiểm tra frontend cần cập nhật snapshot, provenance và bàn giao. Hai repo còn nhiều thay đổi chưa commit; HEAD không định danh đầy đủ working tree.

## Kiểm thử và kiểm tra

| Kiểm tra | Kết quả | Ngày/phạm vi |
|---|---|---|
| Backend regression | **106/106 pass**, không fail/skip | TRX 24/10; xác minh lại evidence ngày 27/10. Source backend trong phạm vi đối chiếu không đổi; không chạy lại 106 test trong lần này |
| Frontend TypeScript | **Pass** sau `next typegen` rồi `tsc --noEmit` | Chạy mới 27/10; lần đầu lỗi route type cũ tham chiếu `app/page.js`. Chỉ tạo lại generated types; không phải test hành vi/browser |
| Frontend clean install | **Fail** | `npm ci --dry-run`: manifest/lockfile lệch; chưa nghiệm thu production build mới |
| Frontend dependency gate tests | **5/5 pass** | Chạy mới 27/10; test logic của gate, không phải component/E2E tests |
| Frontend dependency audit | **Gate pass**, 0 high/critical, còn 1 moderate | Audit mới 27/10 trên dependency graph được lock hiện tại; raw npm exit 1 vì còn finding, gate exit 0. Phải audit lại sau đồng bộ lockfile |
| Production config tests | **4/4 pass** | Chạy mới 27/10; lần đầu lỗi quyền temp sandbox, đổi temp vào workspace rồi đạt |
| Phase 13 integrity tests | **8/8 pass** | Chạy mới 27/10; negative controls của verifier/gói bàn giao |
| Review ZIP | **1.134 file hợp lệ**, 602 input evidence khớp hash | Verifier chạy mới 27/10; `release_ready=false`, không phải chạy lại application tests |
| Migration/restore/restart | 7 nhóm kiểm tra đạt, 11 migrations | Evidence 23/10 trên PostgreSQL fixture; restore 1,281 giây cho fixture 77.935 byte, không phải RTO VPS |
| Load campaign | **Chưa đạt toàn campaign** | 18 run smoke/ramp/spike đạt; soak lỗi, chưa hoàn tất |
| AI thật / authenticated UI / VPS | **Chưa đủ bằng chứng đạt** | Offline walkthrough và fake-provider tests không thay cho các kiểm thử này |

Không cộng 106 + 5 + 4 + 8 thành một bộ test nghiệp vụ chung. Chúng có phạm vi và thời điểm khác nhau. Chưa thấy suite component/browser E2E nghiệp vụ frontend được cấu hình trong repository; scripts audit là kiểm tra công cụ.

## Trạng thái chạy thực tế ngày 27/10

- Docker local: API và các service PostgreSQL, Keycloak, Keycloak PostgreSQL, Redis, RabbitMQ báo healthy; Worker đang Up, chưa suy diễn từ Up rằng mọi tác vụ nền đều đã được nghiệm thu.
- `GET http://localhost:8080/health`: 200 / Healthy.
- `GET /api/v1/events?limit=1`: 200, total 10, trả 1 event.
- Trang chủ `http://localhost:3000`: HTTP 200; đây chỉ là HTTP smoke, chưa kiểm tra tương tác/console trong browser.
- Request không token tới `/api/v1/users/me`, `/api/v1/management/events`, `/api/v1/tickets`: đều 401.
- `/api/v1/assistant/status`: 200, provider OpenAI, model gpt-5.4-mini, `status=fallback`, `confirmationRequired=true`. Theo logic status hiện tại, API đang chạy chưa có API key khả dụng trong cấu hình assistant; không đọc/in giá trị secret.
- Không kết nối/tạo/deploy VPS, không chạy lại load campaign, không commit/tag/push trong lần kiểm kê.

## Thứ tự hoàn thiện đề xuất

1. Đồng bộ package manager/lockfile frontend; clean install, typecheck và production build bằng Node 22; audit dependency sau sửa.
2. Tích hợp Orders/Attendees/check-in/analytics cần dùng trong demo; đánh dấu rõ prototype ngoài phạm vi. Kiểm tra browser có đăng nhập theo từng role, đặc biệt các thay đổi auth mới.
3. Điều tra soak và chạy đủ campaign so sánh; chốt SLO và giải thích p95/p99 trước khi kết luận tối ưu.
4. Cấu hình provider AI thật, chạy/chấm 20 case và hội thoại nhiều lượt, ghi chi phí/quota/fallback.
5. Review diff, chốt cặp commit, CI và image digest; cập nhật gói phase 13 từ bản đã kiểm tra.
6. Khi người dùng sẵn sàng VPS: map host/domain, deploy HTTPS, smoke nghiệp vụ, backup/restore toàn stack và redeploy. Sau đó quay demo thật và hoàn thiện báo cáo theo mẫu trường.

## Người dùng cần cung cấp gì

Không cần thêm thông tin để hoàn tất bản kiểm kê này. Các bước kỹ thuật như sửa lockfile, nối UI/API và điều tra load có thể tiếp tục trên workspace.

- **AI thật:** cấu hình OpenAI API key trong secret/env local; thống nhất ngân sách/quota đánh giá. Không gửi API key hoặc bearer token trong chat. Cần tài khoản Customer và catalog demo hợp lệ khi chạy đánh giá.
- **Tiêu chí nghiệm thu:** SLO mong muốn (p95/p99, tỷ lệ lỗi), mức tải mục tiêu và phạm vi UI bắt buộc cho demo. Nếu điều chỉnh mục tiêu 10.000 VU trong kế hoạch, ghi rõ giới hạn tài nguyên và phạm vi thực nghiệm được chấp nhận.
- **VPS khi sẵn sàng:** SSH alias hoặc host/user, domain app/api/auth/monitoring, quyền DNS và registry/GHCR. Cấu hình SSH key/known_hosts/secrets tại máy hoặc CI, không dán private key/mật khẩu vào chat. Hiện vẫn giữ yêu cầu chỉ setup, chưa cần các giá trị này.
- **Vận hành:** nơi lưu backup ngoài VPS, người/kênh nhận cảnh báo, RPO/RTO mong muốn và chính sách giữ backup; đây là cấu hình cần chốt trước nghiệm thu vận hành.
- **Hồ sơ khóa luận:** mẫu trường, thông tin sinh viên/giảng viên, yêu cầu định dạng/trích dẫn và phạm vi demo được duyệt. Bản Markdown sáu chương hiện là bản thảo kỹ thuật.

## Evidence

- [Nguồn hiện tại so với ZIP 25/10](../artifacts/phase13/verification/project-audit-source-20261027.json).
- [Verifier chạy lại ngày 27/10](../artifacts/phase13/verification/project-audit-package-20261027.json).
- [Audit dependency frontend ngày 27/10](../artifacts/phase13/verification/frontend-dependency-audit-20261027.json).
- [Regression backend ngày 24/10](../artifacts/phase13/verification/phase13-regression.trx).
- [Fixture deployment integration](../artifacts/phase12/deployment-integration.json).
- [Checklist release G1–G7](release/release-checklist.md).
- [Chỉ mục bàn giao phase 13](phase-13-handover.md).
