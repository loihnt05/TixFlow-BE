# TixFlow

TixFlow là hệ thống quản lý và đặt vé cho nhiều sự kiện, được thiết kế để nghiên cứu cơ chế giữ chỗ an toàn khi có nhiều yêu cầu đồng thời và khả năng xử lý lưu lượng cao.

## Kiến trúc khởi tạo

- `TixFlow.Api`: ASP.NET Core API và các module nghiệp vụ.
- `TixFlow.Worker`: tác vụ nền; về sau xử lý hết hạn giữ chỗ, outbox và đối soát.
- `TixFlow.Domain`: thực thể, value object và quy tắc miền.
- `TixFlow.Application`: use case và abstraction.
- `TixFlow.Infrastructure`: tích hợp PostgreSQL, Redis và RabbitMQ.
- Frontend được quản lý trong repository độc lập `tixflow-frontend`.

Ở bản khởi tạo, các endpoint chỉ là lát cắt kiểm tra kiến trúc. Chưa có logic giữ vé thật và chưa kết nối thư viện hạ tầng vào mã nguồn.

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
| GET | `/api/v1/events` | Danh sách sự kiện mẫu |
| GET | `/api/v1/bookings/status` | Trạng thái module đặt vé |
| GET | `/api/v1/assistant/status` | Trạng thái module AI Assistant |

## Bước tiếp theo

1. Thiết kế ERD và migration đầu tiên cho Event, Performance, TicketType, Seat, Hold, Order và Payment.
2. Thêm EF Core/Npgsql, StackExchange.Redis và RabbitMQ client theo abstraction trong Infrastructure.
3. Cài JWT và phân quyền Customer, Organizer, Admin.
4. Xây dựng cơ chế giữ chỗ bằng transaction + optimistic concurrency + idempotency.
5. Chỉ tích hợp AI Booking Assistant sau khi Booking Service đã ổn định.
