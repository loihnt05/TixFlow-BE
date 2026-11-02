# API phục vụ demo và bàn giao

Nguồn đầy đủ: [OpenAPI](openapi.json) xuất từ assembly Release vừa chạy regression, [provenance](openapi-provenance.json). Swagger chỉ mở trong Development; production Nginx chặn Swagger. `docs/api/phase-1.openapi.json` là đặc tả lịch sử, không dùng để suy ra route runtime mới nhất.

Base path `/api/v1`. Dùng bearer JWT của Keycloak đúng audience/role; callback mock dùng service JWT riêng. Không đặt JWT trong URL hoặc commit token. Route có ràng buộc ownership vẫn kiểm tra đối tượng ngay cả khi UI hiển thị nút.

| Bước | Method / route | Lưu ý |
|---|---|---|
| Hồ sơ | GET `/users/me` | JWT, đồng bộ người dùng theo `sub` |
| Tìm kiếm | GET `/events` | Catalog public chỉ công bố event hợp lệ |
| Giữ vé | POST `/holds` | Customer; `Idempotency-Key`; items all-or-nothing |
| Đọc hold | GET `/holds/{holdId}` | Theo chủ sở hữu/quyền hỗ trợ |
| Hủy | POST `/holds/{holdId}/cancellations` | Theo contract/version của hold; hoàn tồn đúng một lần |
| Checkout | POST `/holds/{holdId}/checkout` | Chỉ hold còn hiệu lực; retry không tạo order mới |
| Tạo payment | POST `/orders/{orderId}/payments/mock` | Provider giả lập, không thu tiền thật |
| Callback | POST `/payment-webhooks/mock` | Service JWT; không dùng token Customer |
| Đọc đơn | GET `/orders/{orderId}` | Theo chủ sở hữu/quyền hỗ trợ |
| Vé | GET `/tickets?orderId={id}` | Worker phát hành bất đồng bộ |
| QR | GET `/tickets/{ticketId}/qr` | Chủ vé; Admin hỗ trợ cần lý do được audit |
| Check-in | POST `/tickets/check-ins` | Organizer sở hữu event/Admin; idempotency key |
| Assistant | POST `/assistant/messages` | Customer; trả fallback khi provider không khả dụng |
| Xem đề xuất | POST `/assistant/prepare-hold` | Không giữ tồn |
| Xác nhận AI | POST `/assistant/confirm-hold` | `confirmationToken`, `confirmed: true`; Booking kiểm tra lại |

Schema body, header bắt buộc, responses và route quản trị chi tiết lấy từ OpenAPI. Với retry, giữ nguyên idempotency key và payload; không đổi key chỉ vì client timeout. Với thao tác mới, dùng key mới. `409` có thể là xung đột nghiệp vụ; đọc `code` (hết vé, ghế không còn, quote thay đổi…), không coi mọi `409` là lỗi server. `429` phải tôn trọng `Retry-After`. Không dùng quote UI/cache để quyết định số vé còn.

Để xuất lại sau khi sửa API:

```powershell
dotnet build src/TixFlow.Api -c Release --no-restore
python scripts/Export-Phase13OpenApi.py
```

Script chỉ khởi động assembly trên loopback tạm, tắt provider AI, trỏ DB/Redis vào cổng không có dịch vụ và đọc Swagger. Không gửi request nghiệp vụ; tiến trình được dừng khi xong. OpenAPI không thay thế kiểm thử tính đúng hoặc quyền truy cập.
