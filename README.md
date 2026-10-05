# TixFlow

TixFlow là hệ thống quản lý và đặt vé cho nhiều sự kiện, được thiết kế để nghiên cứu cơ chế giữ chỗ an toàn khi có nhiều yêu cầu đồng thời và khả năng xử lý lưu lượng cao.

## Kiến trúc hiện tại

- `TixFlow.Api`: ASP.NET Core API và các module nghiệp vụ.
- `TixFlow.Worker`: tác vụ nền; về sau xử lý hết hạn giữ chỗ, outbox và đối soát.
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

Authentication và đồng bộ user được kiểm thử với PostgreSQL cách ly, dùng cổng loopback `15433` và dữ liệu tạm:

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
| GET | `/api/v1/bookings/status` | Trạng thái module đặt vé |
| GET | `/api/v1/assistant/status` | Trạng thái module AI Assistant |

## Bước tiếp theo

1. Xây dựng CRUD Organizer/Event/EventSession/TicketType/Seat và public event detail.
2. Xây dựng cơ chế hold bằng transaction + atomic update + idempotency.
3. Bổ sung xử lý expiry, outbox, RabbitMQ và Worker nghiệp vụ.
4. Hoàn chỉnh checkout/payment giả lập, ticket QR và check-in.
5. Chỉ tích hợp AI Booking Assistant sau khi Booking Service đã ổn định.
