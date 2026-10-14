# Giai đoạn 2 — Hợp đồng access token

**Phạm vi:** công việc 1 của giai đoạn 2, dùng cùng cấu hình JwtBearer và policy của công việc 2. Ngày xác nhận: **08/10/2026**.

## 1. Claim bắt buộc

API nhận **access token** do realm `tixflow` phát hành. Không dùng ID token hoặc refresh token để gọi API.

| Claim | Hợp đồng | Nguồn tại Keycloak |
| --- | --- | --- |
| `iss` | Khớp chính xác `Authentication:Authority`; môi trường Docker development là `http://localhost:8180/realms/tixflow`. | Public hostname và realm của Keycloak. |
| `aud` | Chuỗi `tixflow-api` hoặc mảng audience chứa `tixflow-api`. | Mapper `tixflow-api-audience` trên cả hai client. |
| `sub` | Một chuỗi không rỗng; là định danh liên kết local user, không thay bằng email. | Mapper Subject trong default client scope `basic`. |
| `email` | Một chuỗi không rỗng; thông tin hồ sơ, không phải khóa identity. | Default client scope `email`, user property `email`. |
| `preferred_username` | Một chuỗi không rỗng; không dùng làm khóa identity. | Default client scope `profile`, user property `username`. |
| `roles` | Mảng chuỗi; sau khi lọc role nghiệp vụ phải có đúng một role phân biệt hoa/thường: `Customer`, `Organizer` hoặc `Admin`. | Mapper `tixflow-roles`, realm-role mapper, `multivalued=true`. |

API cũng kiểm tra chữ ký **RS256**, thời hạn token và các điều kiện JwtBearer. `MapInboundClaims=false` giữ nguyên tên claim. Các role kỹ thuật của Keycloak không được tính là role nghiệp vụ.

`tixflow-web` và `tixflow-swagger` cùng dùng default scopes `basic`, `profile`, `email`, `web-origins`; `fullScopeAllowed=false`; role scope mapping chỉ gồm ba role nghiệp vụ. Cả hai client dùng Authorization Code và PKCE `S256`, không bật Direct Access Grants. Cấu hình import ghim thuật toán RS256 ở realm và từng client, đồng thời tắt lightweight access token cho hai client để giữ bộ claim hồ sơ cần thiết.

Keycloak mô tả `sub` qua mapper của scope `basic` và việc các client kế thừa mapper từ default scopes trong [Server Administration Guide](https://www.keycloak.org/docs/latest/server_admin/#_protocol-mappers). Tên thuộc tính signing algorithm được công bố trong [Keycloak OIDC configuration constants](https://github.com/keycloak/keycloak/blob/main/server-spi-private/src/main/java/org/keycloak/protocol/oidc/OIDCConfigAttributes.java).

## 2. Một tài khoản, một role nghiệp vụ

- Đăng ký mới nhận `Customer` qua composite role `default-roles-tixflow`.
- Khi cấp `Organizer` hoặc `Admin`, quản trị viên phải gỡ `Customer` trực tiếp **và** gỡ đường kế thừa Customer của tài khoản, bao gồm `default-roles-tixflow` hoặc group/composite khác, rồi gán một role đích.
- Giữ nguyên cấu hình default Customer cho các tài khoản đăng ký mới khác.
- Xem **effective roles** và lấy access token mới sau khi đổi role. Token cũ giữ role tới khi hết hạn; thay đổi role trên Keycloak không viết lại token đã phát hành.
- Keycloak cho phép gán nhiều role; policy của API là nơi từ chối tài khoản có không hoặc nhiều hơn một role nghiệp vụ. Không chọn role theo thứ tự ưu tiên và không suy ra Customer khi thiếu role.
- Organizer và Admin không kế thừa quyền mua vé của Customer. Quyền theo phạm vi Event/ownership vẫn được kiểm tra tại các module nghiệp vụ.

## 3. Bằng chứng xác nhận

### 3.1. Token mới từ cấu hình repository

Đã tạo một Keycloak **26.7.4** tạm, import bản `infrastructure/keycloak/tixflow-realm.json` hiện tại và thêm ba tài khoản demo tạm, mỗi tài khoản có một role. Không thay đổi realm `tixflow` đang chạy ở cổng 8180.

Đã đăng nhập qua Authorization Code + PKCE `S256`, đổi code lấy access token, kiểm tra chữ ký RS256 bằng public key từ JWKS, issuer, audience, thời hạn và các claim bắt buộc:

| Client | Customer | Organizer | Admin |
| --- | --- | --- | --- |
| `tixflow-web` | Đạt | Đạt | Đạt |
| `tixflow-swagger` | Đạt | Đạt | Đạt |

Cả **6 access token mới** đều có `iss`, audience chứa `tixflow-api`, `sub`, `email`, `preferred_username` và đúng một role nghiệp vụ như tài khoản đã cấp. Issuer của phiên kiểm tra riêng là `http://localhost:18180/realms/tixflow`, khớp public hostname của container tạm. Đây là xác nhận cấu hình phát hành token của repository; không phải xác nhận luồng React hoặc `/users/me` hoàn chỉnh.

Token, mật khẩu demo và thông tin cá nhân không được lưu vào tài liệu. Container và dữ liệu demo được dọn sau khi xác nhận.

### 3.2. Realm development đang chạy

Đã đọc cấu hình hiệu lực bằng Admin API trên `http://localhost:8180`, không ghi thay đổi. Cả hai client có đủ:

- Default scopes `basic`, `email`, `profile`, `web-origins`.
- Mapper access-token cho `sub`, `email`, `preferred_username`, audience `tixflow-api` và mảng `roles`.
- `fullScopeAllowed=false`; role scope mapping `Admin`, `Customer`, `Organizer`.
- Standard Flow bật; PKCE `S256`; issuer discovery là `http://localhost:8180/realms/tixflow`.
- Realm default signing algorithm là RS256. Hai client chưa có override signing algorithm/lightweight rõ ràng như bản import mới; đang kế thừa cấu hình mặc định.

Không đăng nhập bằng tài khoản người dùng hiện hữu trong lần xác nhận này. Việc đọc cấu hình hiện hữu không chứng minh mọi tài khoản cũ đã có email hoặc đã được cấp đúng một role; khi gặp lỗi phải kiểm tra effective roles và hồ sơ của tài khoản đó.

## 4. Khi áp dụng vào realm đã tồn tại

`start-dev --import-realm` bỏ qua realm đã tồn tại; khởi động lại container không cập nhật mapper hoặc client settings từ file JSON. Đây là hành vi được mô tả trong [Keycloak realm import](https://www.keycloak.org/server/importExport#_importing_a_realm_during_startup).

Với realm đã tồn tại, cập nhật có chọn lọc trong Admin Console hoặc Admin API, đối chiếu cả hai client:

1. Giữ `basic`, `profile`, `email` là **Default client scopes**; bảo đảm các mapper tương ứng có **Add to access token**.
2. Mapper audience phải thêm `tixflow-api` vào access token; mapper role dùng claim `roles`, kiểu nhiều giá trị và scope chỉ gồm ba role nghiệp vụ.
3. Đặt Access Token Signature Algorithm là `RS256`, Use lightweight access token là tắt; giữ PKCE `S256`.
4. Điền email, username cho tài khoản; xác nhận effective business roles có đúng một role.
5. Lấy token mới. Nếu vẫn thiếu `sub`, sửa scope/Subject mapper và đăng nhập lại; không dùng email thay thế.

Không cần xóa volume hoặc import đè toàn bộ realm để thực hiện các cập nhật này.
