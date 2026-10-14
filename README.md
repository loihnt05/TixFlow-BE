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

Authentication, Hold concurrency, checkout/payment và Worker/QR/check-in được kiểm thử với PostgreSQL cách ly tại loopback `15433`, RabbitMQ test tại `15673` và dữ liệu tạm:

```powershell
docker compose -f docker-compose.test.yml up -d --wait
dotnet test TixFlow.sln
docker compose -f docker-compose.test.yml down
```

Lệnh `down` ở trên không có `-v`; test database dùng `tmpfs` nên không lưu dữ liệu nghiệp vụ.

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

## Bước tiếp theo

Bộ đặc tả actor, business rules, ERD, state/sequence, API và ADR nằm tại [thiết kế Giai đoạn 1](docs/phase-1-design-index.md). Các chính sách sản phẩm đã được người dùng xác nhận; cổng phê duyệt giảng viên được theo dõi trong tài liệu.

Toàn bộ Giai đoạn 2 được ghi tại [JWT, role policy và middleware](docs/phase-2-authentication-foundation.md), [token contract Keycloak](docs/phase-2-token-contract.md) và [biên bản hoàn tất Authentication/current user](docs/phase-2-frontend-current-user.md). Giai đoạn 3 nằm tại [Catalog và quản trị sự kiện](docs/phase-3-catalog-management.md); Giai đoạn 4 nằm tại [Suất diễn, ghế, loại vé và inventory](docs/phase-4-inventory.md); Giai đoạn 5 nằm tại [Hold và kiểm soát đồng thời](docs/phase-5-hold-concurrency.md); Giai đoạn 6 nằm tại [Checkout, Order và payment](docs/phase-6-checkout-payments.md).

Giai đoạn 7 được ghi tại [Expiry, RabbitMQ, vé QR và check-in](docs/phase-7-worker-tickets.md), gồm migration, topology, recovery và các ca kiểm thử. FE giữ nguyên giao diện và lấy vé/QR thật từ API.

1. Đóng băng baseline giai đoạn 8, chạy concurrency/load test quy mô lớn và thu metrics lock wait/deadlock.
2. Chỉ tích hợp AI Booking Assistant sau khi Booking Service đã ổn định.
