# Kiến trúc TixFlow

TixFlow bắt đầu bằng Modular Monolith để giữ độ phức tạp vừa sức khóa luận, nhưng tách ranh giới module rõ ràng để có thể scale nhiều API instance và tách worker.

## Quy tắc phụ thuộc

```text
Api/Worker -> Infrastructure -> Application -> Domain
Api/Worker -> Application
```

Domain không phụ thuộc database, Redis, RabbitMQ hay framework web. Mọi thay đổi trạng thái vé phải đi qua Booking module; AI Assistant chỉ gọi API của module này.

## Module dự kiến

| Module | Trách nhiệm |
|---|---|
| Identity | Đăng nhập, phân quyền và hồ sơ |
| Events | Sự kiện, suất diễn, sơ đồ ghế và loại vé |
| Booking | Khả dụng, giữ chỗ, hết hạn và chống oversell |
| Orders | Đơn hàng, thanh toán giả lập và idempotency |
| Assistant | Điều phối tool gọi API sau xác nhận người dùng |

## Nguồn dữ liệu

PostgreSQL là nguồn sự thật. Redis chỉ dùng cho cache, rate limit hoặc dữ liệu tạm có thể tái tạo. RabbitMQ truyền sự kiện bất đồng bộ; outbox sẽ được bổ sung để tránh mất thông điệp sau khi transaction đã commit.

