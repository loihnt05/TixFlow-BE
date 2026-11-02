# Sơ đồ hiện trạng

Các sơ đồ rút gọn phục vụ thuyết trình, không thay schema đầy đủ. Tên bảng đối chiếu `database/init/001_initial_schema.sql` và migrations 002–011. Hạ tầng production là cấu hình đã chuẩn bị, chưa phải sơ đồ một VPS đã nghiệm thu.

## Hình 1 — Thành phần và ranh giới dữ liệu

```mermaid
flowchart LR
  Browser[Browser Next.js] --> Nginx[Nginx HTTPS]
  Browser --> KC[Keycloak OIDC]
  Nginx --> FE[Next.js standalone]
  Nginx --> API[ASP.NET Core API]
  API --> DB[(PostgreSQL nghiệp vụ)]
  KC --> KDB[(PostgreSQL Keycloak)]
  API --> AI[OpenAI provider tùy chọn]
  Worker[Worker] --> DB
  Worker <--> MQ[RabbitMQ]
  Prom[Prometheus] --> API
  Grafana[Grafana] --> Prom
```

Nguồn: `docker-compose.production.yml`, `src/TixFlow.Api/Program.cs`, Worker. Redis nằm trong stack nhưng không quyết định inventory; cache catalog metadata hiện tại thuộc process, generation invalidation qua PostgreSQL. Keycloak có hostname riêng qua Nginx; các mũi tên browser/auth là luồng logic, không có nghĩa public cổng container.

## Hình 2 — ERD nghiệp vụ rút gọn

```mermaid
erDiagram
  users ||--o{ holds : owns
  users ||--o{ orders : owns
  events ||--o{ event_sessions : schedules
  event_sessions ||--o{ ticket_types : offers
  ticket_types ||--|| ticket_inventories : counts
  holds ||--o{ hold_items : contains
  ticket_types ||--o{ hold_items : reserves
  holds ||--o| orders : checks_out
  orders ||--o{ order_items : contains
  orders ||--o{ payments : attempts
  order_items ||--o{ tickets : issues
  seats ||--o{ seat_allocations : history
  holds ||--o{ seat_allocations : reserves
```

Outbox, inbox, idempotency, audit và projection không vẽ để giữ hình dễ đọc. Một ghế có nhiều allocation lịch sử nhưng chỉ một allocation Held/Sold theo unique index. Cardinality minh họa không biểu diễn hết constraint về trạng thái, TTL hoặc ownership.

## Hình 3 — Payment và phát hành vé

```mermaid
sequenceDiagram
  actor Customer
  participant API
  participant PG as PostgreSQL
  participant Worker
  participant MQ as RabbitMQ
  Customer->>API: Hold (Idempotency-Key)
  API->>PG: Transaction, khóa, conditional reserve
  PG-->>API: Commit hold + ledger
  API-->>Customer: Hold ID + deadline
  Customer->>API: Checkout, payment mock
  API->>PG: Order/payment idempotent
  Note over API,PG: Simulator/callback settlement kiểm tra lại state
  API->>PG: Commit Paid + OrderPaid outbox
  Worker->>PG: Claim outbox bằng lease
  Worker->>MQ: Persistent publish + confirm
  MQ->>Worker: Deliver (có thể lặp)
  Worker->>PG: Transaction inbox + tickets
  PG-->>Worker: Commit
  Worker->>MQ: ACK
  Customer->>API: GET tickets/QR
```

Nguồn: Orders module, [phase 7](../phase-7-worker-tickets.md). Expiry chạy riêng trong Worker, khóa và kiểm tra trạng thái trước hoàn tồn; không phụ thuộc broker đang sẵn sàng.

## Hình 4 — Assistant và sự xác nhận

```mermaid
sequenceDiagram
  actor Customer
  participant UI
  participant Assistant
  participant Catalog
  participant Booking
  Customer->>UI: Nhập nhu cầu
  UI->>Assistant: Message + Customer JWT
  Assistant->>Catalog: Typed search/detail/availability
  Catalog-->>Assistant: Dữ liệu hiện tại
  Assistant-->>UI: Proposal + protected confirmation token
  Note over Assistant,Booking: Prepare chưa giữ tồn, model không có payment tool
  Customer->>UI: Xác nhận giữ vé
  UI->>Assistant: confirmed=true + token
  Assistant->>Assistant: Kiểm tra subject/purpose/expiry
  Assistant->>Booking: Hold với JWT người dùng + stable key
  Booking->>Booking: Kiểm tra quote, quyền và tồn trong transaction
  Booking-->>UI: Hold hoặc conflict
```

Nguồn: `AssistantModule.cs`, `AssistantTokens.cs`, `AssistantBookingApi.cs`, Booking module. Nếu giá thay đổi trả `QUOTE_CHANGED`; nếu provider không sẵn sàng, UI hướng về catalog.
