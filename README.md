# TixFlow

TixFlow là hệ thống quản lý và đặt vé cho nhiều sự kiện, được thiết kế để nghiên cứu cơ chế giữ chỗ an toàn khi có nhiều yêu cầu đồng thời và khả năng xử lý lưu lượng cao.

## Kiến trúc hiện tại

- `TixFlow.Api`: ASP.NET Core API và các module nghiệp vụ.
- `TixFlow.Worker`: hết hạn giữ chỗ, outbox/RabbitMQ, phát hành vé QR và notification giả lập.
- `TixFlow.Domain`: thực thể, value object và quy tắc miền.
- `TixFlow.Application`: use case và abstraction.
- `TixFlow.Infrastructure`: tích hợp PostgreSQL, Redis và RabbitMQ.
- Frontend được quản lý trong repository độc lập `tixflow-frontend`.

Database PostgreSQL lõi đã được kết nối bằng EF Core/Npgsql. Schema khởi tạo gồm tài khoản, sự kiện, suất diễn, ghế/kho vé, hold, order, payment, vé QR, idempotency, webhook, outbox và audit log. Keycloak cung cấp OIDC/OAuth 2.0; API đã xác thực JWT, đồng bộ hồ sơ cục bộ và áp dụng policy `Admin`, `Organizer`, `Customer`.

## Yêu cầu

- .NET SDK 8
- Docker Desktop hoặc Docker Engine có Compose

Repository này đã có Git. Không chạy lại `git init`; kiểm tra nhánh và thay đổi bằng `git status --short --branch`.

## Chạy toàn bộ bằng Docker

```bash
cp .env.example .env
# Fill every blank value in .env with a unique local credential.
docker compose up -d --build
```

Trên PowerShell:

```powershell
Copy-Item .env.example .env
# Fill every blank value in .env with a unique local credential.
.\scripts\Invoke-Phase0Smoke.ps1 -StartStack
```

Sau khi khởi động:

- API info: http://localhost:8080/api/v1/system/info
- API health: http://localhost:8080/health
- Swagger: http://localhost:8080/swagger
- Keycloak discovery: http://localhost:8180/realms/tixflow/.well-known/openid-configuration
- RabbitMQ Management: http://localhost:15672

RabbitMQ and PostgreSQL use the credentials from the ignored `.env` file.

Khi PostgreSQL tạo volume lần đầu, script `database/init/001_initial_schema.sql` tự tạo toàn bộ schema và dữ liệu mẫu. Nếu volume PostgreSQL đã tồn tại từ bản project cũ nhưng chưa có bảng, chạy:

```powershell
Get-Content .\database\init\001_initial_schema.sql -Raw |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U tixflow -d tixflow
```

Không dùng `docker compose down -v` nếu cần giữ dữ liệu hiện có.

`scripts/Invoke-Phase0Smoke.ps1` không xóa container hay volume. Script xác minh Compose, migration, `/health`, Swagger, Keycloak discovery và baseline PostgreSQL 20 bảng. Khi stack đã chạy, có thể kiểm tra lại mà không rebuild:

```powershell
.\scripts\Invoke-Phase0Smoke.ps1
```

## Chạy riêng để phát triển

Khởi động các dịch vụ hạ tầng:

```bash
docker compose up -d postgres redis rabbitmq
# Set ConnectionStrings__Postgres, RabbitMq__Username and RabbitMq__Password
# in your shell from the ignored .env file before starting the API.
dotnet restore TixFlow.sln
dotnet run --project src/TixFlow.Api
```

## Kiểm thử

Testcontainers tự dựng PostgreSQL/RabbitMQ trên cổng ngẫu nhiên, áp dụng schema/seed và dọn tài nguyên sau khi chạy. Chỉ cần Docker đang chạy; không cần `.env` hay stack nghiệp vụ:

```powershell
dotnet test TixFlow.sln -c Release --filter 'Category!=Baseline'
# Baseline có CPU/RAM, SDK, images, DB pool và dataset cố định:
.\scripts\Invoke-Phase8Baseline.ps1 -Mode All
```

`artifacts/phase8/<timestamp>-all/` chứa TRX, manifest SHA-256 và JSON từng request/lock sample/query plan. `docker-compose.test.yml` vẫn có thể dùng để điều tra thủ công, nhưng automated tests không kết nối các cổng cố định đó.

## Endpoint khởi tạo

| Method | Endpoint | Mục đích |
|---|---|---|
| GET | `/health` | Kiểm tra API sống |
| GET | `/api/v1/system/info` | Kiểm tra phiên bản và môi trường |
| GET | `/swagger/v1/swagger.json` | OpenAPI document và OAuth2 security scheme |
| GET | `/api/v1/events` | Danh sách sự kiện `Published` đọc từ PostgreSQL |
| POST | `/api/v1/holds` | Giữ vé/ghế all-or-nothing với idempotency |
| GET | `/api/v1/holds/{holdId}` | Đọc Hold trong phạm vi Customer/Admin |
| POST | `/api/v1/holds/{holdId}/cancellations` | Hủy và giải phóng Hold đúng một lần |
| POST | `/api/v1/holds/{holdId}/checkout` | Chuyển Hold thành Order, không tạo lặp |
| GET | `/api/v1/orders` | Lịch sử đơn hàng Customer/Admin |
| POST | `/api/v1/orders/{orderId}/payments/mock` | Tạo payment attempt giả lập |
| POST | `/api/v1/payment-webhooks/mock` | Callback dành riêng cho service JWT |
| GET | `/api/v1/tickets` | Vé QR của tài khoản hiện tại |
| GET | `/api/v1/tickets/{ticketId}` | Chi tiết vé trong phạm vi chủ sở hữu |
| POST | `/api/v1/tickets/check-ins` | Check-in chống quét trùng, Organizer sở hữu Event hoặc Admin |
| GET | `/api/v1/bookings/status` | Trạng thái module đặt vé |
| GET | `/api/v1/assistant/status` | Trạng thái module AI Assistant |
| GET | `/api/v1/operations/metrics` | Admin xem cache hit/miss và snapshot queue/consumer lag của Worker |

## Bước tiếp theo

Bộ đặc tả actor, business rules, ERD, state/sequence, API và ADR nằm tại [thiết kế Giai đoạn 1](docs/phase-1-design-index.md). Các chính sách sản phẩm đã được người dùng xác nhận; cổng phê duyệt giảng viên được theo dõi trong tài liệu.

Toàn bộ Giai đoạn 2 được ghi tại [JWT, role policy và middleware](docs/phase-2-authentication-foundation.md), [token contract Keycloak](docs/phase-2-token-contract.md) và [biên bản hoàn tất Authentication/current user](docs/phase-2-frontend-current-user.md). Giai đoạn 3 nằm tại [Catalog và quản trị sự kiện](docs/phase-3-catalog-management.md); Giai đoạn 4 nằm tại [Suất diễn, ghế, loại vé và inventory](docs/phase-4-inventory.md); Giai đoạn 5 nằm tại [Hold và kiểm soát đồng thời](docs/phase-5-hold-concurrency.md); Giai đoạn 6 nằm tại [Checkout, Order và payment](docs/phase-6-checkout-payments.md).

Giai đoạn 7 được ghi tại [Expiry, RabbitMQ, vé QR và check-in](docs/phase-7-worker-tickets.md), gồm migration, topology, recovery và các ca kiểm thử. FE giữ nguyên giao diện và lấy vé/QR thật từ API.

1. Baseline giai đoạn 8 và cách tái tạo nằm tại [kiểm thử tính đúng và thực nghiệm](docs/phase-8-baseline.md). Các lần đổi thuật toán/cấu hình/dataset cần baseline version mới.
2. Chỉ tích hợp AI Booking Assistant sau khi Booking Service đã ổn định.

Giai đoạn 9: [tối ưu có kiểm chứng](docs/phase-9-optimization.md), [quyết định kỹ thuật](docs/adr/008-phase9-optimization.md), [đề xuất model AI](docs/ai-provider-proposal.md). Chạy `./scripts/Invoke-Phase9Verification.ps1 -Mode All`; không dùng script giai đoạn 8 để gắn nhãn baseline v1 cho thuật toán mới. Giao diện FE giữ nguyên.

Giai đoạn 10: [thực nghiệm HTTP bằng k6](docs/phase-10-load-testing.md). Chạy `./scripts/Invoke-Phase10Load.ps1 -Variant both -Repetitions 3` để so sánh hai image đã xác minh trong stack cách ly, rồi dùng `scripts/Summarize-Phase10.py` tổng hợp số liệu. Báo cáo phân biệt smoke với chiến dịch ramp/spike/soak và các tiêu chí nghiệm thu còn mở.

Tiếp tục phase 10: [trạng thái và báo cáo load-v2](docs/reports/load-v2/README.md), có candidate Checkout riêng, monitoring Prometheus/Grafana và script campaign smoke/ramp/spike/soak. Xem `artifacts/phase10/campaign-*/status.json` để biết tiến trình đo thực tế; chưa coi việc khởi động campaign là hoàn tất.

Giai đoạn 11: [AI Booking Assistant](docs/phase-11-ai-assistant.md). API/UI, typed tools, xác nhận hold, audit và adapter OpenAI đã được triển khai; xem báo cáo để biết kiểm thử và phần đánh giá model thật còn mở.

Giai đoạn 12: [setup VPS, HTTPS, monitoring, backup/restore và CI/CD](docs/phase-12-deployment.md). Bộ triển khai production đã tách riêng; điền `.env.production.example`, chuẩn bị image digest và chạy `bash deploy/scripts/deploy.sh` trên VPS khi sẵn sàng. Hiện chỉ chuẩn bị setup, chưa triển khai VPS thật.

Giai đoạn 13: [bộ bàn giao review](docs/phase-13-handover.md), gồm báo cáo, sơ đồ, OpenAPI hiện tại, số liệu truy vết về raw và kịch bản demo. Regression mới 106/106 đạt; campaign phase 10 vẫn failed ở soak. Chạy `python scripts/Build-Phase13Evidence.py --package` để đóng gói source/evidence có manifest SHA-256. Xem [checklist release](docs/release/release-checklist.md) trước khi chốt commit/image hoặc nghiệm thu VPS.
