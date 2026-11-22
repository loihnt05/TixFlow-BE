# Lấy access token trong Swagger (Development)

`POST /api/v1/auth/login` chỉ được đăng ký khi API chạy với `ASPNETCORE_ENVIRONMENT=Development`. Body:

```json
{
  "usernameOrEmail": "customer-a@example.test",
  "password": "mật khẩu tài khoản test"
}
```

Request login chỉ có `usernameOrEmail` và `password`; không cần `phoneNumber`. Response `200` gồm `accessToken`, `tokenType`, `expiresIn` (giây), `user` (`sub`, `username`, `email`, `name`, `roles`) và `claims` (toàn bộ claims trong access token do realm Keycloak phát hành). Login xác thực chữ ký, issuer, audience và hạn token theo cấu hình JWT của API trước khi trả `200`. Trong Swagger, bấm **Authorize → bearer**, dán giá trị `accessToken` (không thêm `Bearer `), rồi gọi API private. Sai thông tin đăng nhập trả `401`; nếu Keycloak phát token thiếu định danh hoặc đúng một role nghiệp vụ, endpoint trả `503` thay vì trả token không dùng được. API không trả refresh token và không lưu mật khẩu.

Endpoint này dùng Keycloak client `tixflow-dev-login`, được định nghĩa trong `infrastructure/keycloak/tixflow-realm.json` với Direct Access Grant. Realm import chỉ áp dụng cho Keycloak DB mới; với volume Keycloak cũ, cần tạo/cập nhật client này trong realm trước khi dùng. Client `tixflow-web` và luồng đăng nhập frontend vẫn giữ Authorization Code + PKCE.

Nếu login trả access token nhưng `GET/PATCH /api/v1/users/me` trả `403`, kiểm tra payload access token có claim `roles` chứa đúng một role `Customer`, `Organizer` hoặc `Admin`. Client `tixflow-dev-login` phải có mapper `tixflow-roles` ghi vào access token và, vì `fullScopeAllowed=false`, **Scope → Role scope mappings** phải gồm cả ba role nghiệp vụ. Cấu hình import trong repository đã khai báo các mapping này; realm Keycloak đã tồn tại cần cập nhật trực tiếp trong Admin Console rồi lấy access token mới. Kiểm tra **effective roles** của tài khoản: chỉ một role nghiệp vụ được phép; nhiều role hoặc không có role đều bị API từ chối.

Không dùng endpoint này trong production. Production không đăng ký route; Swagger UI cũng chỉ bật trong Development.
