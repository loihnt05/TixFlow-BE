# Kiến trúc TixFlow

TixFlow bắt đầu bằng Modular Monolith để giữ độ phức tạp vừa sức khóa luận, nhưng tách ranh giới module rõ ràng để có thể scale nhiều API instance và tách worker.

## Quy tắc phụ thuộc

```text
Api/Worker -> Infrastructure -> Application -> Domain
Api/Worker -> Application
```

Domain không phụ thuộc database, Redis, RabbitMQ hay framework web. Mọi thay đổi trạng thái vé phải đi qua Booking module; AI Assistant chỉ gọi API của module này.

## Module đã triển khai

| Module | Trách nhiệm |
|---|---|
| Identity | Đăng nhập, phân quyền và hồ sơ |
| Events | Sự kiện, suất diễn, sơ đồ ghế và loại vé |
| Booking | Khả dụng, giữ chỗ, hết hạn và chống oversell |
| Orders | Đơn hàng, thanh toán giả lập và idempotency |
| Assistant | Điều phối tool gọi API sau xác nhận người dùng |
| Tickets/Worker | Expiry, outbox/inbox, phát hành QR, check-in và mock notification |

## Nguồn dữ liệu

PostgreSQL là nguồn sự thật; inventory được kiểm tra và cập nhật trong transaction với constraint/conditional update. Redis có trong stack nhưng không quyết định tồn kho. Catalog metadata hiện dùng MemoryCache theo process (30 giây, tối đa 256 entry), generation trong PostgreSQL để invalidation; availability đọc live. Rate limiter API theo process, Nginx giới hạn tại gateway.

RabbitMQ truyền sự kiện bất đồng bộ. Transactional outbox, lease/publisher confirm và inbox consumer đã triển khai ở phase 7; redelivery không được nhân đôi vé. Key volume QR và Assistant phải được giữ cùng dữ liệu khi backup/restore. Assistant chỉ chuẩn bị đề xuất; confirm gọi Booking API bằng quyền của Customer.

Các quyết định ban đầu nằm trong [ADR](adr/README.md) và [thiết kế Giai đoạn 1](phase-1-design-index.md). Sơ đồ hiện trạng rút gọn và nguồn implementation ở [bộ bàn giao phase 13](release/diagrams.md). Compose production hiện là setup đã chuẩn bị, chưa nghiệm thu VPS; giới hạn thực nghiệm/AI được ghi tại [báo cáo](release/thesis-report.md).

