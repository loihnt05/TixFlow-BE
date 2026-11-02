# TixFlow — Bộ bàn giao review

Ngày chốt nội dung: 24/10/2026. Bản này dành cho review kỹ thuật và chuẩn bị bảo vệ; chưa phải nghiệm thu production. Xem [trạng thái phase 13](../phase-13-handover.md) và [các gate](release-checklist.md).

[Báo cáo sáu chương](thesis-report.md) · [PowerPoint 11 slide](TixFlow-defense.pptx) · [Kịch bản demo](demo-script.md) · [Fallback offline](offline-demo.html) · [OpenAPI](openapi.json).

Rà soát bổ sung ngày 25/10/2026: [kết quả kiểm tra](review-20261025.json), [video bằng chứng dự phòng 42 giây](TixFlow-evidence-fallback.webm). Video không âm thanh, dùng khi cần trình bày số liệu offline; chưa thay video thao tác ứng dụng hoặc demo 8–12 phút.

## Chạy trên máy mới

Giải nén hai thư mục `tixflow-backend` và `tixflow-frontend` cạnh nhau. Snapshot không có `.git` hoặc secrets; người nhận muốn Git history cần clone repository riêng. Cài .NET SDK 8, Node.js 22 và Docker Engine/Desktop với Linux containers, Compose. Cần mạng để tải dependencies/images trong lần đầu.

Trong backend, copy `.env.example` thành `.env`, điền tất cả thông tin xác thực trống bằng giá trị local riêng, rồi chạy:

```powershell
docker compose config --quiet
docker compose up -d --build
.\scripts\Invoke-Phase0Smoke.ps1
dotnet restore TixFlow.sln --locked-mode
dotnet test TixFlow.sln -c Release --no-restore --filter 'Category!=Baseline'
```

Không reset volume đang có dữ liệu. Trên DB mới, init/migrations do cấu hình Compose và smoke kiểm tra; trên DB cũ, đọc runbook trước khi áp dụng migration. Backend local: API `http://localhost:8080`, Swagger `/swagger`, Keycloak `http://localhost:8180`. Đây là cấu hình development, không đưa các cổng này ra Internet.

Trong frontend:

```powershell
Copy-Item .env.example .env.local
# Đối chiếu API/Keycloak URLs trong file với stack local.
npm ci
npm run lint
npm run build
npm run dev
```

Mở `http://localhost:3000`. Dùng Node 22 theo engines; build Linux Docker đã được kiểm chứng ở phase 12. Build Windows trước đó gặp lỗi tiến trình con Turbopack; chưa coi máy Windows bất kỳ là đã được kiểm chứng. Hai lockfile npm/pnpm đang có trong repo; dùng một package manager nhất quán, không chạy install tự nâng version.

Đăng nhập Customer/Organizer/Admin bằng tài khoản local đã cấu hình. Kiểm tra ngày mở bán/suất diễn trong seed; khi demo sau ngày seed, cập nhật qua API/UI Organizer trên dữ liệu demo. Không chạy load seed lên database có dữ liệu cần giữ. Xem [demo 10 phút](demo-script.md) để nghiệm thu end-to-end.

## VPS khi sẵn sàng

[Runbook phase 12](../phase-12-deployment.md) chứa cấu hình SSH, domain, registry/image digest, HTTPS, deploy, smoke, backup, restore và rollback. Copy `.env.production.example` trên VPS rồi điền giá trị thật trong kho riêng. Không dùng image `:verify`, secret local hoặc realm development cho production. Thiết lập biến repo GitHub và production environment theo runbook trước khi gọi workflow. Lần chuẩn bị này chưa chạy SSH/deploy.

## Tái tạo báo cáo và kiểm tra gói

Cài Python 3.12+ và matplotlib trong môi trường riêng. Từ backend:

```powershell
python scripts/Build-Phase13Evidence.py
python scripts/Build-Phase13Evidence.py --package
python scripts/Build-Phase13Evidence.py --verify artifacts/phase13/packages/<review.zip>
python scripts/Test-Phase13Evidence.py
python scripts/Verify-Phase13Release.py --package artifacts/phase13/packages/<review.zip>
```

Lệnh build cần raw phase 10 và TRX phase 13 tại đúng đường dẫn được giữ trong ZIP. `--verify`, `Test-Phase13Evidence.py` và `Verify-Phase13Release.py` chỉ dùng thư viện chuẩn, không cần Git/Docker/matplotlib, không giải nén và không chạy nội dung ZIP. Verifier kiểm tra hash, TRX, tình trạng campaign, OpenAPI, liên kết tài liệu, số liệu PowerPoint và provenance của fallback; kết quả đạt vẫn giữ `release_ready=false`. Kiểm tra SHA-256 của ZIP bằng receipt `.json` bên cạnh trước khi phân phối. SHA-256 xác minh nội dung; không chứng minh danh tính tác giả nếu receipt bị thay cùng gói.

Rebuild từ ZIP không có Git chỉ dùng `--verify` và đọc báo cáo đã đóng gói. Để tạo một snapshot mới, dùng checkout Git tương ứng, chép evidence vào `artifacts/` rồi chạy build. Không giả lập một clean commit bằng `git init` trong snapshot.

## Đọc bằng chứng

[Báo cáo](thesis-report.md) → [bảng số liệu](measured-results.md) → `evidence-summary.json` → đường dẫn input/raw + SHA-256. Manifest gói còn chứa hash mọi file source. Số liệu phase 8/9, HTTP phase 10 và regression hiện tại là ba nhóm phép đo khác nhau; không gộp thành một benchmark duy nhất.

`offline-demo.html` tự chứa hình, không cần Internet. Mở bằng trình duyệt, dùng phím trái/phải. Đây là walkthrough bằng chứng; video thao tác UI và nghiệm thu máy sạch vẫn nằm trong checklist.

Để kiểm tra lại fallback trên máy có Node/Edge, cài Playwright vào thư mục tools riêng rồi chạy:

```powershell
npm install --prefix artifacts/phase13/browser-tools playwright
$env:RUNTIME_NODE_MODULES=(Resolve-Path artifacts/phase13/browser-tools/node_modules).Path
node scripts/Check-Phase13Offline.mjs
node scripts/Record-Phase13Fallback.mjs
```

Hai script dùng browser profile tạm, context offline, không dùng tài khoản trình duyệt của người chạy. Script thứ hai cần ảnh đã sinh bởi script đầu. Sau khi sửa fallback/ảnh/số liệu, chạy lại hai script trước khi đóng gói; verifier sẽ từ chối evidence browser/video có hash cũ.
