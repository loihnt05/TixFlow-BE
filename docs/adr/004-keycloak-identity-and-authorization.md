# ADR-004: Keycloak quản lý định danh, API kiểm tra quyền nghiệp vụ

- Ngày: **07/10/2026**.
- Trạng thái: **Thiết kế đề xuất để triển khai**; Keycloak/OIDC đã là nền xác thực hiện tại.
- Quyết định sản phẩm liên quan đã xác nhận: một application role mỗi tài khoản, một owner mỗi Organizer, Admin quản trị xuyên Organizer.

## Bối cảnh

TixFlow cần đăng nhập Customer/Organizer/Admin, kiểm tra quyền trên từng đối tượng và ánh xạ định danh ngoài sang FK nội bộ. API đã xác thực JWT, đọc claim `roles`, đồng bộ user theo `sub` và từ chối hồ sơ bị khóa. Realm có web/Swagger public client dùng Authorization Code + PKCE S256; API có audience riêng.

Keycloak cung cấp discovery và các endpoint OIDC; API có thể xác thực JWT bằng khóa công khai từ realm. [Keycloak — Securing applications and services with OpenID Connect](https://www.keycloak.org/securing-apps/oidc-layers).

## Quyết định

Keycloak quản lý credential, đăng nhập, phiên và refresh token. API nhận access token, kiểm tra chữ ký, issuer, audience và lifetime trước khi truy cập dữ liệu riêng. Chỉ claim từ token đã xác thực được dùng làm định danh/quyền; `userId`, `organizerId` hoặc role trong request không thay thế người gọi. Production dùng HTTPS và redirect URI cụ thể.

`users.identity_subject` lưu `sub` trong realm đã cấu hình, unique và ánh xạ sang `users.id`. Không tự nối tài khoản theo email; email trùng với subject khác trả conflict để xử lý chủ động. Thiết kế hiện chỉ có một issuer; nếu hỗ trợ nhiều realm phải chuyển uniqueness sang `(issuer, subject)` bằng migration trước. `users.role` là snapshot phục vụ hồ sơ, phải khớp role duy nhất sau đồng bộ; nếu không đồng bộ được thì từ chối. Không nâng quyền dựa vào cột này khi token thiếu quyền.

Sau khi lọc các role ứng dụng `Customer`, `Organizer`, `Admin`, token phải có **đúng một** role. Role kỹ thuật khác của Keycloak không tính vào phép đếm. Không có role hoặc có nhiều hơn một role thì từ chối thao tác nghiệp vụ; không chọn role ưu tiên rồi ngầm cho qua. Enforcement này đã được bổ sung ngày **08/10/2026** trong default policy và các named policy. Khi cấp Organizer/Admin, vận hành phải gỡ Customer được kế thừa từ `default-roles-tixflow`, tránh còn quyền Customer qua composite role.

Organizer/Admin không được đi qua policy Customer để mua vé. Muốn mua vé phải dùng tài khoản Customer riêng. Không định nghĩa Admin như kế thừa mọi policy; endpoint quản trị/check-in liệt kê quyền Admin riêng theo ma trận actor đã chốt.

### Quyền trên object

| Tài nguyên/thao tác | Điều kiện |
|---|---|
| Hold/Order/Ticket của Customer | `authenticated sub -> users.id` phải bằng `user_id` của tài nguyên. |
| Event của Organizer | `events.organizer_id -> organizers.owner_user_id` phải trỏ tới user hiện tại. Một owner có một Organizer, Organizer có nhiều Event. |
| Check-in của Organizer | Kiểm tra Ticket thuộc session/event của chính Organizer đó và điều kiện check-in trong state contract. |
| Admin cancel/check-in | Role Admin được phạm vi toàn hệ thống; vẫn kiểm tra state/version, deadline/policy và ghi audit actor/object/lý do khi hủy. |
| AI Assistant | Dùng danh tính và quyền của người gọi qua API; model/tool không được tự chọn actor hoặc SQL trực tiếp. |

Ownership được kiểm tra trong cùng command transaction với thay đổi nghiệp vụ, không chỉ dựa vào việc UI ẩn nút. Quyền Admin không bỏ qua invariant sức chứa hoặc cho phép hồi sinh Order hết hạn. Người dùng đã chốt hủy/check-in ngày **07/10/2026**: Admin có thể hủy vé Paid, thời gian check-in là `[session.starts_at_utc - 60 phút, session.ends_at_utc)`, parent bị hủy làm vé không hợp lệ ngay. Xác nhận giảng viên vẫn **Decision Pending**, hạn **11/10/2026**.

JWT có thời hạn nên role vừa thay đổi có thể chưa phản ánh trong access token đã cấp. TixFlow kiểm tra `users.status` local cho khóa tài khoản hiện tại; cần thu hồi phiên/làm mới token khi đổi role và ghi rõ khoảng trễ của token. Không tuyên bố logout làm mọi JWT đã cấp mất hiệu lực tức thì khi API chỉ kiểm tra chữ ký cục bộ.

## Phương án cân nhắc và hệ quả

| Phương án | Đánh giá |
|---|---|
| Tự lưu password và phát JWT | Không chọn; Keycloak đã sở hữu vòng đời định danh. `password_hash`/`refresh_tokens` legacy được giữ nhưng không dùng cho luồng mới. |
| Đồng bộ account theo email | Không chọn vì email không thay thế subject đã xác thực; nguy cơ gán nhầm ownership. |
| RBAC là đủ, không kiểm tra object | Không chọn vì hai Organizer có cùng role nhưng sở hữu Event khác nhau. |

Cần vận hành thêm Keycloak và cấu hình issuer/audience đúng giữa URL trình duyệt và mạng container. Token hợp lệ với khóa đã cache có thể được xác thực khi provider gián đoạn, nhưng login/refresh hoặc tải khóa mới có thể thất bại; không chuyển sang tin token không kiểm chứng.

## Tiến độ triển khai — cập nhật 08/10/2026

- Đã có enforcement đúng một application role ở default policy và từng policy `Admin`, `Organizer`, `Customer`; thiếu/sai/nhiều role trả 403 trên endpoint bảo vệ trước khi đồng bộ local user.
- `IdentityProfile.PrimaryRole` chỉ nhận role duy nhất đã kiểm tra; đã bỏ ưu tiên Admin/Organizer và fallback Customer khi thiếu role.
- JwtBearer giữ nguyên tên claim (`MapInboundClaims = false`), dùng `preferred_username` làm Name và `roles` làm Role; yêu cầu `sub`, `email`, `preferred_username` hợp lệ. Thiếu claim định danh trả 401, không thay `sub` bằng email.
- Pipeline: routing → CORS → authentication → authorization → local profile/status → endpoint. Public/anonymous endpoint không đồng bộ user. Ownership trong command không được phụ thuộc vào Items được tạo sau authorization để viết policy chạy trước middleware đó.
- JWT/profile middleware đã có; object authorization, audit command và quy trình cấp/gỡ role còn cần triển khai.
- Callback Mock cần scheme/audience/scope dịch vụ riêng theo API contract; không chạy human profile sync yêu cầu email/role Customer/Organizer/Admin cho machine principal này.
- `refresh_tokens` và `password_hash` được migration 002 đánh dấu legacy; không tạo luồng refresh/password thứ hai trong API.

## Kịch bản nghiệm thu dự kiến

| ID | Tình huống | Kết quả phải đạt |
|---|---|---|
| ADR-004-TC-01 | Sai issuer/audience, token hết hạn hoặc chữ ký không hợp lệ. | Không truy cập API riêng tư. |
| ADR-004-TC-02 | Token có Organizer + Customer, kể cả qua composite. | Từ chối nghiệp vụ và không tạo Hold/Order. |
| ADR-004-TC-03 | Organizer A sửa Event hoặc check-in vé của B. | Bị từ chối theo ownership; Admin được phép theo quyền đã chốt. |
| ADR-004-TC-04 | Hai lần đăng nhập đầu tiên cùng subject chạy đồng thời. | Một local user; không nhân đôi hồ sơ. |
| ADR-004-TC-05 | Subject mới dùng email đã thuộc subject cũ. | Conflict; không chiếm các Order/Event của hồ sơ cũ. |
| ADR-004-TC-06 | Token còn hạn nhưng local user bị khóa. | Middleware chặn nghiệp vụ. |
| ADR-004-TC-07 | Không có application role hoặc token chỉ chứa role kỹ thuật. | Không tự trở thành Customer nhờ fallback PrimaryRole. |

