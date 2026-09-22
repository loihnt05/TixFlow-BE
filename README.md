# TixFlow

TixFlow là hệ thống quản lý và đặt vé cho nhiều sự kiện, được thiết kế để nghiên cứu cơ chế giữ chỗ an toàn khi có nhiều yêu cầu đồng thời và khả năng xử lý lưu lượng cao.

## Kiến trúc khởi tạo

- `TixFlow.Api`: ASP.NET Core API và các module nghiệp vụ.
- `TixFlow.Worker`: tác vụ nền; về sau xử lý hết hạn giữ chỗ, outbox và đối soát.
- `TixFlow.Domain`: thực thể, value object và quy tắc miền.
- `TixFlow.Application`: use case và abstraction.
- `TixFlow.Infrastructure`: tích hợp PostgreSQL, Redis và RabbitMQ.
- Frontend được quản lý trong repository độc lập `tixflow-frontend`.

Database PostgreSQL lõi đã được kết nối bằng EF Core/Npgsql. Schema khởi tạo gồm tài khoản, sự kiện, suất diễn, ghế/kho vé, hold, order, payment, vé QR, idempotency, webhook, outbox và audit log. Logic API giữ vé sẽ được xây dựng ở bước tiếp theo trên schema này.

## Yêu cầu

- .NET SDK 8
- Node.js 22 trở lên
- Docker Desktop hoặc Docker Engine có Compose

Sau khi giải nén mã nguồn, chạy `git init -b main` ngay trong thư mục `tixflow-backend`.

## Chạy toàn bộ bằng Docker

```bash
cp .env.example .env
docker compose up --build
```

Trên PowerShell:

```powershell
Copy-Item .env.example .env
docker compose up --build
```

Sau khi khởi động:

- API info: http://localhost:8080/api/v1/system/info
- API health: http://localhost:8080/health
- RabbitMQ Management: http://localhost:15672

RabbitMQ Development:

```text
Username: tixflow
Password: tixflow_dev
```

PostgreSQL Development:

```text
Host: localhost
Port: 5432
Database: tixflow
Username: tixflow
Password: tixflow_dev
```

Khi PostgreSQL tạo volume lần đầu, script `database/init/001_initial_schema.sql` tự tạo toàn bộ schema và dữ liệu mẫu. Nếu volume PostgreSQL đã tồn tại từ bản project cũ nhưng chưa có bảng, chạy:

```powershell
Get-Content .\database\init\001_initial_schema.sql -Raw |
    docker compose exec -T postgres psql -v ON_ERROR_STOP=1 -U tixflow -d tixflow
```

Không dùng `docker compose down -v` nếu cần giữ dữ liệu hiện có.

## Chạy riêng để phát triển

Khởi động các dịch vụ hạ tầng:

```bash
docker compose up -d postgres redis rabbitmq
dotnet restore TixFlow.sln
dotnet run --project src/TixFlow.Api
```

## Endpoint khởi tạo

| Method | Endpoint | Mục đích |
|---|---|---|
| GET | `/health` | Kiểm tra API sống |
| GET | `/api/v1/system/info` | Kiểm tra phiên bản và môi trường |
| GET | `/api/v1/events` | Danh sách sự kiện `Published` đọc từ PostgreSQL |
| GET | `/api/v1/bookings/status` | Trạng thái module đặt vé |
| GET | `/api/v1/assistant/status` | Trạng thái module AI Assistant |

## Bước tiếp theo

1. Cài JWT và phân quyền Customer, Organizer, Admin trên các bảng `users` và `refresh_tokens`.
2. Xây dựng CRUD Event/EventSession/TicketType/Seat.
3. Xây dựng cơ chế giữ chỗ bằng transaction + atomic update + idempotency.
4. Bổ sung Redis và RabbitMQ theo abstraction trong Infrastructure.
5. Chỉ tích hợp AI Booking Assistant sau khi Booking Service đã ổn định.
