# Nghiệm thu phần 0 — 02/11/2026 (Asia/Saigon)

Kết quả: **PASS cho preflight local Windows/Node 24**. Đây là điều kiện chuẩn bị phần 1, chưa phải chứng nhận nghiệp vụ, image Linux mới hoặc VPS. Các container ứng dụng đang chạy được tạo từ bản trước; regression chạy source hiện tại với Testcontainers cách ly. Cần build/recreate ứng dụng từ bản đã khóa trước khi nghiệm thu E2E phần 1.

## Bản mã và môi trường

- Backend: nhánh `dev`, commit `acf39082b637670a913889b78ae25e4c33256183`.
- Frontend: nhánh `dev`, commit `0c477bcde5e065ec66463c501830efebc95bc98a`.
- Cả hai working tree có thay đổi chưa commit. Manifest JSON ghi status và SHA-256 từng file tracked/untracked không bị ignore; không chứa nội dung `.env` hay credentials. Không tự tạo commit.
- Windows 11 Home Single Language 10.0.26200 x64; Ryzen 5 5600H, 6 cores/12 threads; RAM 16,487,870,464 bytes (~15.36 GiB).
- Lúc chụp: C còn 118,102,843,392 bytes; D còn 79,953,883,136 bytes. Chi tiết và container dùng chung máy nằm trong `environment.json`.
- Node 24.18.0; npm 11.18.0; pnpm 11.9.0; .NET SDK dùng trong backend 8.0.424; PowerShell 7.6.5; Docker client/server 29.8.0, Linux containers.
- Python host đang trỏ Windows App Alias chưa dùng được. Runtime thay thế đã kiểm tra: `tixflow-acceptance-python:20261102`, Python 3.12.16, matplotlib 3.10.7; đã xuất PNG smoke thành công. Dockerfile: `scripts/acceptance-python.Dockerfile`.

## Kết quả và evidence

Evidence gốc: `artifacts/acceptance/20261102-preflight-01/` (gitignored, lưu local). Manifest sau chỉnh tài liệu/script: `artifacts/acceptance/20261102-preflight-final/`.

| Kiểm tra | Actual | Evidence |
|---|---|---|
| npm ci | Exit 0, 188 packages | npm-ci.log |
| pnpm lock | frozen lockfile-only exit 0; chưa chạy full pnpm install/build | pnpm-lock-check.log |
| next typegen, TypeScript | Pass | typegen.log, lint.log |
| Audit gate unit tests | 5/5 pass | audit-tests.log |
| Production build Windows Node 24 | Exit 0; BUILD_ID `K9oaWlSbcdOhoNebD1yA1` | build.log, Frontend-results.jsonl |
| npm vulnerability gate | Pass | npm-audit.json, npm-audit-gate.log |
| dotnet locked restore | Pass | restore.log |
| Backend regression, bỏ Category=Baseline | 106 passed, 0 failed, 0 skipped | regression.trx, regression.log |
| Backend vulnerability gate | Exit 0; không high/critical | dotnet-audit.json |
| Frontend secret history scan | 0 findings | frontend-secrets.json |
| Backend secret history scan | 2 false positives đã đối chiếu; scan lại 0 findings | backend-secrets.json, backend-secrets-reviewed.json |
| Python/chart | Pass | python.log, python-smoke.png |
| Health/discovery/CORS | Pass | public-config.json |
| Live OIDC client settings | Public client; callback/logout khớp; PKCE S256 | live-oidc-client.log |

Hai cảnh báo gitleaks là suffix của `jdbc:postgresql://keycloak-postgres:5432/keycloak` trong hai commit cũ, không có credential. `.gitleaksignore` chỉ chứa hai fingerprint chính xác; workflow backend truyền rõ đường dẫn ignore. Báo cáo ban đầu được giữ nguyên. Secret scan theo workflow quét lịch sử Git; không đại diện cho mọi file ignored trên máy.

## Cấu hình public đã kiểm tra

API `http://localhost:8080`; authority `http://localhost:8180/realms/tixflow`; client `tixflow-web`; callback `http://localhost:3000/auth/callback`; logout `http://localhost:3000/`. API chấp nhận CORS origin `http://localhost:3000`. Build đã đọc `.env.local`. Login/refresh/logout end-to-end vẫn thuộc phần 2.

## Chạy lại

Dùng thư mục evidence mới cho mỗi lượt. Chạy `scripts/Invoke-AcceptancePreflight.ps1 -Evidence <absolute-new-directory> -Stage Frontend`, rồi `-Stage Backend`, `-Stage Inventory`; script dừng khi lệnh lỗi và lưu mã thoát. Chạy `scripts/Test-AcceptancePublicConfig.ps1` cùng Evidence để ghi kiểm tra cấu hình local. Không tự coi một lệnh sau thành công là xóa lỗi lệnh trước.

Python cho script báo cáo: `docker run --rm -v "${PWD}:/work" tixflow-acceptance-python:20261102 python scripts/Plot-Phase10.py --help` khi đứng tại backend. Với script gọi API host, dùng địa chỉ phù hợp Docker như `host.docker.internal`, không dùng localhost của container.

Phần 0 đã đủ điều kiện chuẩn bị kiểm thử tiếp. Các lỗi mock/catalog/check-in ở phần 1 cần xử lý và nghiệm thu riêng; không được suy từ preflight PASS rằng các luồng mua vé đã đạt.
