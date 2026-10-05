# Phase 0 — baseline kỹ thuật và runbook

Tài liệu này là nguồn kiểm chứng cho baseline kỹ thuật. Nó mô tả trạng thái source, cách tái lập kiểm tra và các phần chưa phải chức năng sản phẩm hoàn chỉnh. Kết quả một lần chạy không thay thế cho việc chạy lại ở commit sẽ được gắn tag.

## Phạm vi

Phase 0 chốt nền tảng phát triển có thể tái lập cho hai repository: cấu hình cục bộ không chứa secret, Docker Compose cho hạ tầng, schema/seed/migration PostgreSQL, Keycloak/OIDC ở môi trường Development, health/Swagger/discovery, và smoke test/test tự động cơ bản.

Phase này không bao gồm luồng giữ chỗ giao dịch, đơn hàng, thanh toán, phát hành vé, tải cao, triển khai production hoặc AI Assistant có khả năng thay đổi trạng thái.

## Kết quả kiểm tra gần nhất

Ngày 05/10/2026, source chưa commit đã được kiểm tra trong môi trường local:

- `Invoke-Phase0Smoke.ps1` pass: API health, Swagger/OIDC scheme, Keycloak discovery, migration và PostgreSQL đều đạt; database có 20 bảng và 1 sự kiện `Published` seed.
- `dotnet test TixFlow.sln --no-restore` pass 21/21 với PostgreSQL test cô lập.
- `npm run lint` và `npm run build` pass cho frontend; server standalone trả trang chủ HTTP 200 trong smoke local.
- Stack chính đã được dừng lại sau kiểm tra; không xóa container, image hay volume.

Đây là bằng chứng kỹ thuật cho source đang sửa. Chưa tạo tag baseline vì các thay đổi cần được review, commit, và kiểm tra OIDC browser thủ công ở chính SHA sẽ được gắn tag.

## Dịch vụ và cổng cục bộ

Không đưa mật khẩu, token, connection string đầy đủ hoặc dữ liệu `.env` vào tài liệu, terminal log được chia sẻ hay Git. Sao chép `.env.example` thành `.env` và điền giá trị local riêng; `.env` và `.env.local` đã được ignore.

| Dịch vụ | Host port | Mục đích |
| --- | --- | --- |
| Frontend Next.js | `3000` | Giao diện local và callback OIDC |
| API | `8080` | API, `/health`, Swagger trong Development |
| PostgreSQL ứng dụng | `5433` → container `5432` | Dữ liệu nghiệp vụ local |
| Redis | `6379` | Cache/dữ liệu tạm cho hạ tầng |
| RabbitMQ AMQP | `5672` | Kết nối message broker |
| RabbitMQ Management | `15672` | Quản trị RabbitMQ local |
| Keycloak HTTP/OIDC | `127.0.0.1:8180` | Realm `tixflow`, discovery và trình duyệt đăng nhập local |
| Keycloak management health | `127.0.0.1:9001` | Health nội bộ, chỉ loopback |
| PostgreSQL cho test | `127.0.0.1:15433` | Database tạm thời từ `docker-compose.test.yml` |

Worker không mở cổng host. Cổng test chỉ phục vụ test integration, không phải database phát triển.

## Kiểm tra tái lập

Chạy các lệnh dưới đây bằng PowerShell. Lưu thời điểm chạy, SHA của cả hai repository và output thành công/thất bại vào issue hoặc pull request baseline; không dán `.env` hay token.

```powershell
# Từ workspace root; tạo .env một lần và điền secret local ngoài Git.
Set-Location .\tixflow-backend
if (-not (Test-Path .env)) { Copy-Item .env.example .env }

docker compose up -d --build
docker compose ps

# Smoke runner trả exit code khác 0 nếu một tiêu chí không đạt.
.\scripts\Invoke-Phase0Smoke.ps1

# API/Keycloak probes có thể chạy độc lập khi cần xác định lỗi.
Invoke-RestMethod http://localhost:8080/health
Invoke-WebRequest http://localhost:8080/swagger/index.html -UseBasicParsing
Invoke-RestMethod http://localhost:8180/realms/tixflow/.well-known/openid-configuration
```

```powershell
# Từ tixflow-backend; test dùng PostgreSQL cô lập ở port 15433.
docker compose -f docker-compose.test.yml up -d --wait postgres-test
dotnet test TixFlow.sln
docker compose -f docker-compose.test.yml down --remove-orphans
```

```powershell
# Từ tixflow-frontend; điền các biến public OIDC/API theo .env.example trước khi chạy app.
Set-Location ..\tixflow-frontend
npm ci
npm run lint
npm run build
```

Với kiểm tra trình duyệt, chạy `npm run dev`, truy cập `http://localhost:3000`, xác nhận liên kết Events/health hoạt động, sau đó kiểm tra callback OIDC và `/account` bằng một tài khoản phát triển đã được tạo trong Keycloak. Không lưu thông tin đăng nhập mẫu trong source hoặc tài liệu.

## Ma trận module

`Completed` nghĩa là phần source của baseline đã có cùng kiểm tra tái lập. `Partial` nghĩa là có integration hoặc scaffold nhưng chưa đủ use case/kiểm tra end-to-end. `Missing` nghĩa là chưa có implementation chức năng.

| Module | Trạng thái | Bằng chứng source / giới hạn |
| --- | --- | --- |
| Repository, cấu hình local và cấu trúc solution | Completed | Backend/frontend là Git repository riêng; `.env` và `.env.local` bị ignore; có file `.env.example`. |
| Hạ tầng phát triển | Partial | `docker-compose.yml` định nghĩa PostgreSQL, Redis, RabbitMQ, Keycloak, API và Worker; cần smoke ở SHA ứng viên để xác nhận runtime. |
| Schema, seed và migration PostgreSQL | Completed | `001_initial_schema.sql`, seed phát triển, mapping EF Core và migration `002_keycloak_identity.sql` có thể chạy lặp. |
| Identity/OIDC và hồ sơ local | Partial | Realm Keycloak, JWT validation, role policy, local-user sync và test integration có sẵn; vẫn cần kiểm tra browser login thật ở môi trường baseline. |
| Events | Partial | Có endpoint read-only cho sự kiện `Published`; chưa có CRUD cho organizer hoặc UI danh sách sự kiện. |
| Booking | Partial | Schema, domain model và endpoint trạng thái tồn tại; chưa có hold/expire/confirm transaction hay bảo vệ oversell. |
| Orders, payments và tickets | Missing | Mới có schema/domain foundation; chưa có API hay luồng nghiệp vụ hoàn chỉnh. |
| AI Booking Assistant | Missing | Chỉ có endpoint thông báo `planned`; chưa có tool orchestration hoặc thao tác thay đổi trạng thái. |
| Frontend | Partial | Có shell Next.js, OIDC provider, trang account và role guard; chưa có trải nghiệm browse/booking hoàn chỉnh. |
| Production readiness và vận hành | Missing | Chưa có TLS/secret manager, CI artifact, observability, load test, backup/restore drill hoặc deployment plan. |

## Rủi ro kỹ thuật

| Rủi ro | Tác động | Giảm thiểu / owner tiếp theo |
| --- | --- | --- |
| Compose hoặc cổng local không chạy ở SHA cần chốt | Không thể tái lập baseline | Chạy smoke runner, lưu SHA/output, xử lý conflict cổng trước tag. |
| Secret local vô tình vào Git hoặc log | Lộ thông tin môi trường | Chỉ dùng file ignore, quét diff trước commit và thay secret ngay khi lộ. |
| Init schema và migration additive lệch nhau ở database cũ/mới | Lỗi runtime hoặc lỗi dữ liệu | Chạy test PostgreSQL cô lập; xác minh cả volume mới và volume hiện hữu trước release. |
| Keycloak nội bộ và authority trình duyệt có địa chỉ khác nhau | Discovery/JWT/login browser có thể lỗi cấu hình | Giữ `MetadataAddress` nội bộ chỉ cho container, kiểm tra discovery qua `8180` và smoke browser. |
| Booking chưa có transaction/concurrency control | Không được tuyên bố hỗ trợ đặt vé hoặc chống oversell | Không mở endpoint mutate trước khi hold/idempotency/load test đạt Definition of Done. |
| Chưa có CI, monitoring hay quy trình production | Baseline chỉ phù hợp Development | Đưa pipeline, TLS, secret manager và telemetry vào Phase tiếp theo trước deploy. |

## Backlog ưu tiên và Definition of Done

| Ưu tiên | Hạng mục | Definition of Done |
| --- | --- | --- |
| P0 | Chốt baseline có bằng chứng | `Invoke-Phase0Smoke.ps1`, `dotnet test TixFlow.sln`, frontend lint/build đều pass; SHA, thời điểm và output được ghi nhận; hai working tree sạch. |
| P0 | Gắn baseline tag cho hai repository | Tag annotated cùng nhãn Phase 0 được tạo ở đúng SHA đã kiểm tra, được push lên remote và được ghi vào bằng chứng. |
| P0 | Kiểm tra OIDC browser end-to-end | Login/callback/logout và `/api/v1/users/me` thành công với Keycloak local; không có token/secret trong log được lưu. |
| P1 | Transactional booking core | API hold, expire, confirm có authorization, idempotency, transaction/locking, test concurrent và không oversell. |
| P1 | Event management/UI | Organizer CRUD events/sessions/ticket types có validation, authorization, UI và test. |
| P1 | Order/payment/ticket | Tạo order từ hold, payment adapter/mock idempotent, issue QR hash và test webhook lặp. |
| P2 | Vận hành production | CI, migration release procedure, secret manager, TLS, metrics/logs/tracing, backup/restore và load-test threshold được nghiệm thu. |

## Checklist nghiệm thu baseline

- [ ] Hai repository ở working tree sạch; SHA được ghi lại.
- [ ] File secret vẫn bị ignore; diff không có password, token hay connection string chứa credential.
- [ ] Compose phát triển healthy và smoke runner pass tại SHA cần chốt.
- [ ] `/health`, Swagger và discovery Keycloak pass; API truy vấn được PostgreSQL.
- [ ] `dotnet test TixFlow.sln` pass với PostgreSQL test cô lập.
- [ ] `npm run lint` và `npm run build` pass ở frontend.
- [ ] OIDC browser login/callback/logout và `/api/v1/users/me` được kiểm tra thủ công bằng tài khoản local an toàn.
- [ ] Ma trận, rủi ro và backlog trong tài liệu này được cập nhật theo kết quả chạy thực tế.
- [ ] Baseline tag đã được tạo và push ở cả backend lẫn frontend.

## Gắn tag baseline

Chỉ gắn tag sau khi checklist pass trên đúng commit. Dùng cùng một nhãn ngày cho cả hai repository để đối chiếu; ví dụ `phase-0-baseline-20261005`. Thay nhãn ví dụ bằng ngày/đợt nghiệm thu thực tế.

```powershell
# Chạy từ workspace root sau khi đã commit tất cả thay đổi thuộc baseline.
git -C .\tixflow-backend status --short
git -C .\tixflow-frontend status --short

git -C .\tixflow-backend tag -a phase-0-baseline-20261005 -m "Phase 0 baseline verified"
git -C .\tixflow-frontend tag -a phase-0-baseline-20261005 -m "Phase 0 baseline verified"

git -C .\tixflow-backend push origin phase-0-baseline-20261005
git -C .\tixflow-frontend push origin phase-0-baseline-20261005
```

Nếu một lệnh kiểm tra thất bại hoặc working tree không sạch, không tạo tag. Sửa nguyên nhân, chạy lại toàn bộ kiểm tra liên quan và chỉ tag commit đã được xác minh.
