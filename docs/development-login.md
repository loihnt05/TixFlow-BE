# Lấy access token trong Swagger (Development)

`POST /api/v1/auth/login` chỉ được đăng ký khi API chạy với `ASPNETCORE_ENVIRONMENT=Development`. Body:

```json
{
  "usernameOrEmail": "customer-a@example.test",
  "password": "mật khẩu tài khoản test"
}
```

Response `200` gồm `accessToken`, `tokenType` và `expiresIn` (giây). Trong Swagger, bấm **Authorize → bearer**, dán giá trị `accessToken` (không thêm `Bearer `), rồi gọi API private. Sai thông tin đăng nhập trả `401`; API không trả refresh token và không lưu mật khẩu.

Endpoint này dùng Keycloak client `tixflow-dev-login`, được định nghĩa trong `infrastructure/keycloak/tixflow-realm.json` với Direct Access Grant. Realm import chỉ áp dụng cho Keycloak DB mới; với volume Keycloak cũ, cần tạo/cập nhật client này trong realm trước khi dùng. Client `tixflow-web` và luồng đăng nhập frontend vẫn giữ Authorization Code + PKCE.

Không dùng endpoint này trong production. Production không đăng ký route; Swagger UI cũng chỉ bật trong Development.
