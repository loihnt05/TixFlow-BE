# Vận hành Virtual Waiting Room

## Trước khi mở một đợt bán

Áp dụng migration `014_waiting_room_policies.sql`, `015_waiting_room_policy_version.sql` và `016_waiting_room_admission_capacity.sql`. Kiểm tra API `/health/ready`, PostgreSQL, Redis có persistence AOF, frontend checkout nhận được `join/status`, và scraper nội bộ đọc `/internal/metrics` khi `Monitoring:Enabled=true`. Policy mặc định là `Disabled`, nên việc deploy code không tự bật hàng chờ.

Admin hoặc Organizer sở hữu sự kiện đọc:

```http
GET /api/v1/management/event-sessions/{sessionId}/waiting-room-policy
Authorization: Bearer <token>
```

Response có `state`, `maxGrantsPerSecond`, `maxConcurrentAdmissions`, `version` và `queueDepth`. `queueDepth: null` nghĩa là không đọc được Redis; **không** xem như hàng chờ rỗng. Việc cấp lượt chỉ chạy khi policy `Active`, sự kiện `Published` và session `OnSale`.

Để bật, gửi đúng `version` vừa đọc và lý do vận hành (10–500 ký tự):

```http
PUT /api/v1/management/event-sessions/{sessionId}/waiting-room-policy
Authorization: Bearer <token>
Content-Type: application/json

{"state":"Active","maxGrantsPerSecond":5,"maxConcurrentAdmissions":500,"expectedVersion":0,"reason":"Open controlled sale for launch"}
```

Server trả `409 POLICY_VERSION_CONFLICT` khi có người khác sửa trước; đọc lại policy rồi quyết định. Bật `Active` trả `503` nếu Redis không sẵn sàng, hoặc `409 SALES_WINDOW_CLOSED` nếu đợt bán chưa mở. Mỗi lần thay đổi ghi `audit_logs` trong cùng transaction với policy, gồm actor, trạng thái/rate trước và sau, version, lý do, queue depth quan sát được và IP.

## Chuyển trạng thái

| State mới | Ảnh hưởng |
| --- | --- |
| `Active` | Controller cấp lượt theo budget Redis chung giữa các replica và giới hạn số grant chưa dùng còn hiệu lực. Hold mới cần admission token. |
| `Paused` | Không cấp lượt mới; khách đã được cấp lượt vẫn có thể tạo Hold bằng token còn hạn. Dùng khi cần dừng cấp lượt tạm thời. |
| `Closed` | Chặn join và Hold mới; replay Hold đã commit vẫn hoạt động. Status của entry cũ chuyển `Rejected` khi client poll (cache trạng thái tối đa 100 ms). |
| `Disabled` | Mở đường mua trực tiếp, không yêu cầu admission token. Entry cũ chuyển `Rejected` khi poll. Chỉ dùng khi đã chủ động quyết định bỏ Gate; lý do được audit. |

Khi xử lý sự cố, ưu tiên `Paused` hoặc `Closed`; cả hai có thể được ghi ngay cả lúc Redis không kết nối được. Sau khi Redis hồi phục, kiểm tra `queueDepth`, lỗi controller và kết quả Hold trước khi chuyển lại `Active`. Không dùng `Disabled` như cách tự động chữa lỗi Redis vì nó bỏ Gate.

## Quan sát và cảnh báo

Prometheus scrape API nội bộ mỗi 15 giây. Các metric không gắn nhãn session để tránh tăng cardinality:

- `tixflow_waiting_room_queue_depth`: tổng số entry trong queue Redis của policy chưa Disabled, lấy mẫu khoảng 5 giây/lần trên mỗi API replica. Có thể gồm entry hết hạn chưa được dọn.
- `tixflow_waiting_room_active_policies`: số policy Active của session đang mở bán trên replica.
- `tixflow_waiting_room_controller_last_success_seconds`: thời điểm vòng controller gần nhất hoàn thành.
- `tixflow_waiting_room_controller_failures_total`: số vòng controller lỗi; log chỉ ghi loại exception.

`deploy/monitoring/alerts.yml` cảnh báo khi queue >100 entry trong 5 phút, controller Active không hoàn thành vòng trong 30 giây, hoặc controller có lỗi. Quan sát thêm tỷ lệ 5xx HTTP, độ trễ status/Hold, Paid→Ticket, RabbitMQ backlog và bất biến inventory. Với nhiều replica, dùng `max` cho queue depth vì mỗi replica quan sát cùng Redis; không cộng các gauge này.

## Điều kiện bật cho khách thật

Chỉ chuyển policy production sang `Active` sau khi tập validation độc lập trên tài nguyên tương đương production đạt mọi ngưỡng tính đúng và độ trễ. Benchmark local không thay thế thử nghiệm soak dài, lỗi mạng, failover Redis và diễn tập rollback trong môi trường triển khai thực tế.
