# Giai đoạn 2 — Authentication và hồ sơ người dùng

**Hoàn tất: 11/10/2026. Phạm vi: toàn bộ 7 công việc của Giai đoạn 2.**

## 1. Luồng frontend

Frontend dùng trực tiếp `keycloak-js` 26.2.4. Hai thư viện OIDC cũ `oidc-client-ts` và `react-oidc-context` đã được gỡ để chỉ còn một nguồn trạng thái xác thực.

Luồng trình duyệt:

1. `AppAuthProvider` khởi tạo client public `tixflow-web` bằng public authority trong `.env.local`.
2. Adapter dùng `flow: standard`, `pkceMethod: S256`, response mode `fragment`, scope `openid profile email` và redirect URI `/auth/callback`.
3. Trước redirect, frontend chỉ lưu đường dẫn quay về đã kiểm tra cùng origin trong `sessionStorage`.
4. Keycloak đổi authorization code lấy token; access/ID/refresh token do adapter giữ trong memory, không ghi vào local/session storage.
5. Callback xóa khỏi luồng điều hướng và quay lại đường dẫn đã lưu. `/account` gửi access token tới `GET /api/v1/users/me`.

`useApi` gọi `updateToken(30)` trước request, gộp các lần refresh đồng thời vào một Promise và chỉ retry một lần khi API trả 401. Token không được ghi vào log hoặc browser storage. Lỗi refresh được trình bày như phiên hết hạn; lỗi kết nối API được phân biệt với 401.

`onTokenExpired` chủ động refresh token. Nếu refresh thất bại, adapter xóa token trong memory và giao diện yêu cầu đăng nhập lại. Logout gọi end-session endpoint của Keycloak rồi quay về đúng `http://localhost:3000/`; URL này giữ dấu `/` cuối để khớp cấu hình client.

Cấu hình development:

```dotenv
NEXT_PUBLIC_API_URL=http://localhost:8080
NEXT_PUBLIC_OIDC_AUTHORITY=http://localhost:8180/realms/tixflow
NEXT_PUBLIC_OIDC_CLIENT_ID=tixflow-web
NEXT_PUBLIC_OIDC_REDIRECT_URI=http://localhost:3000/auth/callback
NEXT_PUBLIC_OIDC_POST_LOGOUT_REDIRECT_URI=http://localhost:3000/
```

`NEXT_PUBLIC_OIDC_AUTHORITY` phải kết thúc bằng `/realms/{realm}`. Đây là public client; không đặt client secret trong biến `NEXT_PUBLIC_*`.

## 2. Current-user API và đồng bộ PostgreSQL

`GET /api/v1/users/me` yêu cầu default authenticated policy. Sau JwtBearer và authorization, `LocalUserMiddleware` tạo `IdentityProfile`, rồi `LocalUserSynchronizer` thực hiện một PostgreSQL upsert có tham số:

- Khóa ánh xạ duy nhất là `identity_subject = sub`.
- `ux_users_identity_subject` phân xử hai first-login đồng thời cho cùng subject.
- `ON CONFLICT (identity_subject) DO UPDATE` cập nhật email, normalized email, tên và role snapshot; giữ nguyên local `id` và `status`.
- Không tìm hoặc nhận tài khoản theo email. Subject mới dùng email đã tồn tại nhận 409.
- Unique normalized email bảo đảm hai subject đồng thời không thể cùng nhận một email.
- User Keycloak mới có `password_hash = NULL`; mật khẩu và refresh token không lưu tại database nghiệp vụ.

Response lấy email, display name và role từ row vừa đồng bộ; `sub` lấy từ token đã xác thực:

```json
{
  "id": "local-user-uuid",
  "sub": "keycloak-subject",
  "email": "user@example.com",
  "name": "Display name",
  "roles": ["Customer"]
}
```

User local `Suspended` hoặc `Disabled` vẫn bị 403 và không được upsert chuyển lại `Active`.

## 3. Protected route và điều hướng theo role

| Khu vực | Role được phép |
|---|---|
| `/account` và `/account/settings` | Admin, Organizer hoặc Customer đã xác thực |
| `/account/tickets`, `/account/orders`, `/account/favorites` | Customer |
| `/checkout` và `/checkout/success` | Customer |
| `/organizer/**` | Organizer |
| `/admin` | Admin |

Header và footer chỉ hiện lối tắt giao dịch cho Customer, workspace/tạo Event cho Organizer và dashboard quản trị cho Admin. Mỗi route vẫn có `ProtectedRoute`; ẩn menu không thay thế kiểm tra quyền. Trang role gọi access probe của API để xác nhận policy phía server.

Hồ sơ `/account` lấy tên, email, role và local user id từ `GET /api/v1/users/me`. Các danh sách Event/ticket/order trên giao diện vẫn là mock của các module nghiệp vụ sau.

## 4. CORS, redirect và mạng Docker

Kiểm tra trên stack Docker thực tế xác nhận:

- Public issuer và endpoint trình duyệt là `http://localhost:8180/realms/tixflow`.
- API lấy metadata ở `http://keycloak:8080/.../.well-known/openid-configuration`; discovery nội bộ vẫn công bố public `issuer` nhưng trả `jwks_uri` nội bộ có port `8080`.
- API container gọi được cả metadata và JWKS nội bộ; JWKS có khóa ký `RS256`.
- Client `tixflow-web` chỉ dùng standard flow, PKCE S256, redirect URI `http://localhost:3000/auth/callback`, web origin `http://localhost:3000` và post-logout URI `http://localhost:3000/`.
- Preflight từ `http://localhost:3000` nhận `Access-Control-Allow-Origin`; origin ngoài danh sách không nhận header này.
- Cấu hình HTTP trên chỉ dành cho `Development`. Staging/Production từ chối authority hoặc metadata HTTP khi khởi động.

## 5. Bộ kiểm thử

- `dotnet build TixFlow.sln`: thành công, 0 warning và 0 error.
- Toàn bộ API tests với PostgreSQL 16 cách ly: **27/27 đạt**. Bộ này gồm 401 khi không có token hoặc sai issuer/audience/signature/thời hạn/`sub`/`email`/`preferred_username`; 403 cho sai role, không có role hoặc nhiều role nghiệp vụ; lọc role kỹ thuật; request lặp/20 request đồng thời cùng subject; hai subject tranh cùng normalized email; profile update giữ nguyên local id; email conflict không liên kết seed; suspended user; cấu hình claim mapping và CORS.
- `npm run lint`: TypeScript thành công.
- `npm run build`: Next.js 16.3.4 production build thành công; các route `/login`, `/register`, `/auth/callback` và `/account` được tạo.
- Docker build thành công; API, Worker, PostgreSQL, Redis, RabbitMQ và Keycloak đều healthy.
- Trình duyệt thật hoàn tất login Customer → `/account` → `/api/v1/users/me`, hiển thị hồ sơ local; truy cập `/organizer` hiện 403 UX; logout về trang chủ. Tài khoản Keycloak và row PostgreSQL dùng cho phép thử đã được xóa.

Máy kiểm tra đang dùng Node 24 nên package manager phát cảnh báo engine; production build vẫn đạt. Repository yêu cầu Node 22 và Dockerfile dùng Node 22.

## 6. Cổng nghiệm thu

| Cổng | Kết quả |
|---|---|
| Customer đăng nhập và `/users/me` trả 200 | Đạt qua trình duyệt và stack Docker thật. |
| Request không token trả 401 | Đạt qua HTTP runtime và automated test. |
| Sai role trả 403 | Đạt ở API policy test và ProtectedRoute. |
| Không tạo user trùng | Đạt với request lặp, 20 request đồng thời cùng subject và unique conflict giữa hai subject. |

Không còn Decision Pending trong phạm vi Giai đoạn 2.
