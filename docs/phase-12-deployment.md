# Phase 12 Deploy VPS và hardening

Ngày chuẩn bị: 23/10/2026. Theo yêu cầu hiện tại, phase này bàn giao **bộ setup sẵn**; người dùng sẽ điền VPS, SSH user, domain và registry sau. Chưa tạo VPS, kết nối SSH, đổi DNS, cấp chứng chỉ thật hay publish image. Không đánh dấu các gate vận hành trên VPS là đã hoàn thành.

## Thành phần đã chuẩn bị

| Thành phần | Vị trí | Hành vi |
|---|---|---|
| Production Compose | `docker-compose.production.yml` | Tách hẳn development; chỉ Nginx publish 80/443; volume, giới hạn CPU/RAM/PID, restart và log rotation |
| Cấu hình đầu vào | `.env.production.example` | Domain, image references và secret để trống; file thật bị ignore |
| Kiểm tra và render | `deploy/scripts/prepare.py` | Chặn placeholder, injection, secret yếu/trùng, application image không có digest; render realm/Nginx không chứa secret |
| Reverse proxy | `deploy/nginx.conf.template` | HTTPS, HSTS, rate limit, forwarding từ đúng một proxy; chặn metrics, Swagger và Keycloak admin public |
| Migration job | `deploy/scripts/migrate.sh` | Khóa advisory cùng một session; checksum; mỗi migration và ledger cùng transaction |
| Deploy và rollback | `deploy/scripts/deploy.sh`, `rollback.sh` | Backup trước migration; manifest digest; smoke; rollback image ứng dụng |
| Backup và restore thử | `backup.sh`, `restore-drill.sh` | Hai PostgreSQL database, khóa QR/Data Protection, SHA-256; restore sang DB tạm |
| Monitoring | `deploy/monitoring/` | Prometheus, Grafana dashboard; API, RabbitMQ, Keycloak, host; 5 alert rules |
| Tác vụ định kỳ | `deploy/systemd/` | Backup hằng ngày; kiểm tra gia hạn TLS hai lần/ngày |
| CI/CD | `.github/workflows/production.yml`, `deploy.yml` | Regression, secret/dependency scan, build/push image SHA; deploy thủ công qua environment production |

API/Worker dùng Serilog JSON; log request chỉ chứa route template, method, status, thời gian và correlation ID do server sinh. Không thêm body/query/JWT vào log. API metrics mặc định tắt; production bật `/internal/metrics`, chỉ được scrape trong Docker network. Nginx không public đường dẫn này. API/Worker/Frontend chạy user không phải root. Worker healthcheck theo heartbeat sau khi lấy được mẫu processing metrics; smoke còn kiểm tra broker khả dụng.

Redis chỉ là cache trên mạng nội bộ. PostgreSQL và RabbitMQ không publish port. User Docker có quyền tương đương root trên host; chỉ cấp nhóm `docker` cho deploy/operator tin cậy. API dùng HTTPS public discovery của Keycloak; VPS phải resolve và truy cập được domain auth của chính nó. Không hạ `RequireHttpsMetadata` để né lỗi DNS/TLS.

## Điền cấu hình khi đã có VPS

Chuẩn bị Linux x86_64, Docker Engine/Compose plugin, Python 3, Bash, `flock`, `tar`, `sha256sum`, Git và đồng bộ NTP. Điểm xuất phát đề xuất 4 vCPU/8 GB RAM/80 GB SSD; xác nhận tài nguyên thực tế trước demo. Tổng giới hạn memory không phải lượng RAM luôn sử dụng. Subnet `172.29.12.0/24` phải không trùng VPN/LAN/Docker network khác; nếu đổi, sửa đồng bộ IP Nginx và trusted proxy.

Tạo user `deploy`, SSH key và thư mục `/opt/tixflow/backend` thuộc user này. Cấu hình firewall/provider firewall cho SSH từ IP quản trị và public TCP 80/443. Chỉ tắt password/root SSH sau khi đã thử đăng nhập bằng key ở phiên khác. Bật cập nhật bảo mật theo chính sách VPS. Không chạy `down -v` trên project production.

Clone backend vào `/opt/tixflow/backend`, checkout commit đã được CI kiểm tra. Production Compose có project name riêng `tixflow-production` nên không nhận volume của project development. Nếu muốn đưa dữ liệu development lên production, thực hiện một đợt chuyển dữ liệu có backup và review riêng; migration runner không tự nhận một schema cũ không có ledger.

Tạo bốn DNS A record: app, api, auth, monitoring trỏ về VPS. Chỉ tạo AAAA khi đường IPv6/firewall/proxy đã được kiểm tra. Với proxy DNS/CDN, bắt đầu bằng DNS-only để nghiệm thu TLS trực tiếp và IP rate limit; chưa cấu hình tin cậy thêm một lớp proxy.

```bash
cd /opt/tixflow/backend
cp .env.production.example .env.production
chmod 600 .env.production
```

Điền domain không kèm scheme/path. Sinh **mỗi secret riêng** bằng `openssl rand -hex 32`. Parser chấp nhận cú pháp `KEY=value` không có quote, khoảng trắng hay `$`; không `source` file này trong shell. Không đưa secret vào frontend/GitHub variables. `.env.production`, backup và rendered artifacts đều bị loại khỏi Git và Docker build context. Docker environment có thể được đọc bởi operator có quyền Docker; file secret không thay thế phân quyền host.

Trong frontend repository, đặt GitHub repository variables `APP_DOMAIN`, `API_DOMAIN`, `AUTH_DOMAIN`. Workflow frontend nhúng các URL công khai lúc build; đổi domain thì phải build lại frontend. Merge code đã review vào `main` ở cả hai repo. Lấy ba reference `ghcr.io/...@sha256:...` từ job summary để điền `API_IMAGE`, `WORKER_IMAGE`, `FRONTEND_IMAGE`. Dùng đúng image đã test, không dùng `latest`. Với registry private, đăng nhập GHCR trên VPS bằng token chỉ có quyền pull và password-stdin.

Các infrastructure tag trong template là điểm xuất phát, không phải tuyên bố không có CVE. Review bản vá trước triển khai. `prepare.py --lock-images` pull và chuyển toàn bộ image sang digest, lưu tại `deploy/rendered/images.env`; bản deploy tiếp theo lại dùng input đã được operator review. Không tự đổi major PostgreSQL/Keycloak.

## Deploy lần đầu và redeploy

```bash
python3 deploy/scripts/prepare.py
bash deploy/scripts/deploy.sh
```

`prepare.py` chỉ xác minh/render, không SSH hay dựng VPS. `deploy.sh` chạy trên VPS đích và thực hiện:

1. Lấy khóa deploy, validate input, pull/khóa digest và kiểm tra Compose.
2. Dừng Nginx/API/Worker/Frontend để tạo cửa sổ bảo trì; khởi động DB/cache/broker và đợi healthy.
3. Backup cả TixFlow và Keycloak cùng khóa ký; một lỗi backup dừng deploy trước migration.
4. Chạy migration job một lần, lưu log, kiểm tra invariant inventory/seat allocation.
5. Nếu chưa có certificate, Certbot dùng standalone port 80 cấp một chứng chỉ SAN cho bốn domain. DNS và ACME phải truy cập được VPS.
6. Khởi động toàn stack, đợi health, kiểm tra Nginx và smoke HTTPS/CORS/auth boundaries/Worker/Prometheus.
7. Chỉ khi pass mới tạo `releases/<timestamp>/completed-at.txt` và cập nhật `releases/current`.

Đây là triển khai demo có downtime chủ động, không phải rolling deploy không gián đoạn. Nếu lỗi sau khi dừng traffic, script trả lỗi và giữ evidence; đọc `releases/<timestamp>/migration.log`, trạng thái container và runbook rollback. Không tự rollback schema hoặc ghi đè dữ liệu. Không chỉnh sửa migration đã áp dụng; thêm migration mới. File init chỉ áp dụng cho DB sạch; migration ledger và checksum được ghi atomically cùng thay đổi schema. Keycloak realm import chỉ chạy với realm chưa tồn tại; sửa domain/client sau này cần cập nhật realm hiện tại bằng công cụ admin, không trông chờ import ghi đè.

Image API/Worker mới cần volume keys thuộc UID của user `app` (1654 trong image .NET 8 đã dùng); volume mới được Docker khởi tạo từ thư mục đã chown trong image. Compose development đặt rõ user root để giữ tương thích với key volumes cũ, còn Compose production dùng user non-root trong image. Nếu chuyển volume từ bản chạy root, backup rồi sửa owner của đúng volume keys trước; không chown toàn bộ thư mục dữ liệu PostgreSQL.

## Tài khoản và smoke nghiệp vụ

Realm production giữ PKCE S256, audience `tixflow-api`, mapper roles và default Customer. Bỏ mọi user seed, client Swagger và callback development. Tự đăng ký Customer qua UI; tạo tài khoản Organizer/Admin qua Keycloak admin CLI chạy trong container. Không public `/admin` và realm master qua Nginx.

```bash
source deploy/scripts/common.sh
dc exec keycloak /opt/keycloak/bin/kcadm.sh config credentials \
  --server http://localhost:8080 --realm master --user operator
# kcadm hỏi password tương tác; không đưa password vào command history.
```

Khi đổi quyền Customer thành Organizer/Admin, bảo đảm token có đúng **một** application role; bỏ role Customer (kể cả composite mặc định) trước khi gán role khác. Cấu hình SMTP nếu cần reset password/email verification; template chưa cấu hình mail provider.

`smoke.sh` kiểm tra public catalog, health, 401 khi thiếu token, CORS allowlist, chặn metrics/Swagger/admin, OIDC issuer, Grafana login, DB invariant, Worker sample mới và Prometheus targets. Sau startup có thể cần chờ một chu kỳ scrape 15 giây trước khi chạy lại smoke nếu target đang khởi tạo. Smoke này không thay thế nghiệm thu đăng nhập và giao dịch:

- Đăng nhập/logout/refresh Customer trong browser thật; xác nhận callback HTTPS.
- Tìm event đang mở bán, giữ vé, checkout, payment mock, vé QR và check-in bằng đúng Organizer.
- Hủy một hold và để một hold khác hết hạn; kiểm tra tồn được trả đúng.
- Restart/redeploy rồi xem lại order/ticket, đăng nhập lại và kiểm tra QR cũ.
- Kiểm tra AI fallback khi chưa có key; chỉ cấu hình key/model nếu cần demo AI.

Seed có mốc thời gian cố định từ các phase trước; review/update lịch mở bán qua API quản trị nếu chạy demo vào thời điểm khác. Không chạy load test mạnh trên VPS demo khi chưa có cửa sổ vận hành riêng.

## Backup và phục hồi

```bash
bash deploy/scripts/backup.sh
bash deploy/scripts/restore-drill.sh /opt/tixflow/backend/backups/<timestamp>
```

Backup dùng pg_dump custom format, kiểm tra TOC, tar khóa QR/Data Protection và SHA-256 trước khi đánh dấu complete. Database dump nhất quán theo snapshot từng DB; hai database không tạo thành snapshot phân tán. Khi deploy, ứng dụng đã dừng; backup định kỳ chạy online. Backup DB và khóa cùng bộ để không mất khả năng đọc ảnh QR/vé cũ.

Restore drill tạo DB tên `restore_drill_*`, restore cả hai dump lần lượt, kiểm tra inventory/seat invariant và bảng realm, rồi chỉ xóa DB tạm do chính script tạo. Không restore đè DB live. File `restore-drill.json` lưu thời gian restore database và tuổi backup thực tế; đây chưa phải RTO toàn hệ thống hoặc bằng chứng đăng nhập lại thành công. Thực hiện thêm smoke API/browser trên môi trường phục hồi cách ly để đo RTO/RPO vận hành.

Giữ ít nhất bảy backup thành công gần nhất; script giữ tất cả để tránh xóa nhầm. Theo dõi dung lượng và chỉ prune bản cũ sau khi xác minh bản sao off-host. Mã hóa hoặc lưu trong storage có quyền truy cập hạn chế; chuyển một bản off-host sau mỗi ngày. Backup tại cùng VPS không bảo vệ được mất VPS. Cất riêng `.env.production`, chứng chỉ, manifest image và key material trong kho quản trị an toàn.

Phục hồi thảm họa: giữ nguyên VPS/volumes lỗi để điều tra; dựng stack cách ly từ release phù hợp, restore cả DB và giải nén keys vào đúng volumes với owner của user `app`; chạy invariant và full smoke, ghi thời gian, sau đó mới chuyển DNS/traffic. Không ghi đè live DB bằng một lệnh restore tự động không có lựa chọn rõ ràng.

Cài các systemd unit sau khi sửa `User` và đường dẫn nếu khác mặc định:

```bash
sudo install -m 644 deploy/systemd/tixflow-* /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now tixflow-backup.timer tixflow-tls.timer
systemctl list-timers 'tixflow-*'
```

Lịch backup là 19:00 UTC (02:00 Việt Nam ngày kế tiếp); TLS kiểm tra hai lần/ngày. Chạy `renew --dry-run` qua Certbot sau lần cấp chứng chỉ đầu. Xem journal của hai service và xử lý lỗi; systemd timers không tự gửi email. Prometheus có alert rules và Grafana hiển thị `ALERTS`; chưa cấu hình Alertmanager/email/Slack vì chưa có người nhận/kênh vận hành.

## Rollback và CI/CD

```bash
bash deploy/scripts/rollback.sh /opt/tixflow/backend/releases/<known-good-timestamp>
```

Rollback chỉ chuyển ba application image về digest của release đã pass; giữ schema và infrastructure hiện tại. Điều kiện: migration mới phải backward-compatible với image cũ. Không tự downgrade PostgreSQL/Keycloak/Grafana hoặc undo SQL. Nếu migration không tương thích, giữ maintenance và dùng quy trình phục hồi cách ly hoặc fix-forward. Các thay đổi Compose/Nginx/config cần checkout phiên bản đã review riêng; rollback image không rollback cấu hình. Với lần deploy đầu chưa có known-good release, sửa nguyên nhân rồi chạy lại; không giả lập một rollback thành công.

Backend CI chạy locked restore, regression, test config, Docker migration/restore, shell syntax, dependency scan mức high/critical, secret scan và build/push image SHA. Frontend CI chạy npm ci, typecheck, build, dependency gate, secret scan và build/push với domain công khai. Các workflow chưa được dispatch từ máy này.

Hardening dependency: Next.js nâng từ 16.3.4 lên 16.4.2 để vá các advisory runtime high/critical; npm/pnpm lockfiles đã đồng bộ. SSH.NET transitive của Testcontainers được override bằng 2026.0.0 trong test project. Frontend audit giữ một ngoại lệ được review cho [braces GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm), hiện chưa có bản vá: chỉ chấp nhận khi toàn bộ dependency chain nằm trong `devDependencies` của Tailwind 3 dùng để đọc glob từ repository tin cậy. Không nhận glob/CSS đầu vào từ người dùng để build. Ngoại lệ không áp dụng nếu package vào runtime, nếu phát sinh advisory khác, hoặc mức critical. Các finding moderate còn được lưu trong audit artifact; không tuyên bố toàn bộ dependency tree sạch. Nâng Tailwind 4 cần một đợt chuyển đổi CSS/UI riêng.

Để bật CD sau này:

1. Tạo GitHub environment `production`, cấu hình **required reviewer** và chỉ cho deploy từ `main`. YAML `environment` tự nó không tạo approval rule; phải bật trong GitHub settings.
2. Đặt environment secrets `SSH_HOST`, `SSH_USER`, `SSH_PRIVATE_KEY`, `SSH_KNOWN_HOSTS`. Lấy host fingerprint qua kênh tin cậy; không dùng StrictHostKeyChecking=no.
3. Chuẩn bị `/opt/tixflow/backend` ở đúng commit SHA được chọn trong workflow, working tree sạch, `.env.production` chứa ba digest đã test; đăng nhập registry nếu private.
4. Dispatch workflow **Deploy prepared VPS** và reviewer phê duyệt. Workflow từ chối checkout remote khác SHA, SSH không xác minh host, hoặc repo remote có thay đổi chưa commit; gọi script có backup, migration, smoke.

CD này cố ý deploy checkout đã chuẩn bị trên VPS; không tự `git reset --hard`, upload secret hay thay file `.env.production`. Khi dùng SSH user khác `deploy`, sửa systemd units tương ứng. Workflow hardcode đường dẫn chuẩn `/opt/tixflow/backend`; đổi đồng bộ nếu dùng thư mục khác.

## Bằng chứng kiểm tra và gate còn lại

Kết quả tại máy phát triển được ghi trong `artifacts/phase12/` (không commit dữ liệu kiểm thử/secret fixture):

| Kiểm chứng | Kết quả ngày 23/10/2026 |
|---|---|
| Backend regression sau cập nhật dependencies | 106/106 pass, `phase12-final.trx` |
| Cấu hình/realm production | 4/4 Python tests pass |
| Dependency gate frontend | 5/5 tests pass; audit gate pass với đúng ngoại lệ build-only đã mô tả |
| Dependency backend | Không còn finding high/critical trong báo cáo `backend-audit.json` |
| Migration/restore/restart | Pass 7 nhóm kiểm tra trong PostgreSQL cách ly; 11 migration, restore database fixture 1,281 giây |
| Compose/Nginx/Prometheus và CI | Config hợp lệ; 5 alert rules hợp lệ; shell scripts qua kiểm tra cú pháp; 3 workflow mới qua actionlint |
| Image Linux | API, Worker, Frontend build thành công; khóa ký có quyền ghi bằng user non-root |
| Frontend runtime | Next.js 16.4.2, HTTP 200 tại `/`, UID 1000 |

Build Next.js trực tiếp trên Windows gặp lỗi tiến trình con Turbopack; Docker Linux với `npm ci` sạch đã build/typecheck thành công. Hai lockfile npm/pnpm đã cập nhật; chạy install theo lockfile nếu tiếp tục phát triển trên máy hiện tại. Chứng chỉ tự ký và config fixture chỉ dùng để validate đã được dọn, không để lại làm cấu hình deploy thật. Thời gian restore nêu trên chỉ là fixture nhỏ, không suy ra RTO VPS.

Các gate còn chờ hạ tầng thật: DNS/HTTPS và tự gia hạn, browser login/payment/ticket, backup/restore cả stack cùng keys, restart/redeploy trên VPS, quyền firewall/SSH, registry/CI/CD approval và kênh gửi cảnh báo. Chỉ đóng nghiệm thu vận hành phase 12 khi lưu được evidence các mục này. Việc chuẩn bị setup không thay thế các gate còn mở của phase 10–11.

## Nguồn kỹ thuật

- [Docker Compose production](https://docs.docker.com/compose/how-tos/production/)
- [Keycloak reverse proxy](https://www.keycloak.org/server/reverseproxy)
- [Keycloak health checks](https://www.keycloak.org/observability/health)
- [Serilog ASP.NET Core integration](https://github.com/serilog/serilog-aspnetcore)
- [prometheus-net](https://github.com/prometheus-net/prometheus-net)
