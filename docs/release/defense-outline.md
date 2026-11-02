# Nội dung deck bảo vệ — 11 slide

Deck editable được tạo trong chat ở phase 13 và xuất thành [TixFlow-defense.pptx](TixFlow-defense.pptx); nội dung nguồn này nằm trong gói để tiếp tục biên tập. Thời lượng lời dẫn khoảng 10 phút, điều chỉnh theo demo. Không thêm kết quả chưa đo để lấp gate còn mở.

| Slide | Nội dung trên slide | Lời dẫn / nguồn |
|---:|---|---|
| 1 | TixFlow — đặt vé an toàn khi có yêu cầu đồng thời; review candidate | Nêu payment mock và VPS mới setup. Nguồn: thesis-report, phase-13-handover |
| 2 | Không oversell; retry không tạo giao dịch trùng; hoàn tồn/phát hành idempotent | Minh họa khách tranh ghế cuối, timeout/retry và payment đua expiry. Nguồn: phase 5–7 |
| 3 | Next.js/Browser → Nginx → API → PostgreSQL; Worker/RabbitMQ; OIDC/monitoring | PostgreSQL quyết định inventory; sơ đồ rút gọn không thể hiện mọi network. Nguồn: Compose production, diagrams |
| 4 | Hold → payment mock → outbox → inbox/ticket → ACK | Delivery có thể lặp; uniqueness bảo vệ hiệu ứng. Nguồn: phase 7, Orders module |
| 5 | Search → preview → confirm → Booking; fallback | Prepare không giữ tồn; token gắn subject/purpose/expiry. Chưa đánh giá AI thật. Nguồn: phase 11 |
| 6 | Regression / TestServer / HTTP là ba nhóm phép đo | 2 lần lặp ở phase 9; 3 mỗi variant/profile ở phase 10; mixed workload và cùng host. Nguồn: manifests, Summarize-Phase10.py |
| 7 | Biểu đồ phase 9 median p95 theo 100/500/1.000 request | Dữ liệu giây baseline 6,24/14,44/27,44; optimized 3,33/7,38/12,65. Không gọi là production SLA. Nguồn: reference-concurrency.json và concurrency.json |
| 8 | Biểu đồ ramp p95 browse/hold/checkout/callback | Có cải thiện và có tail regression; checkout p99 cũng tăng. Bảng đủ endpoint/p99 trong measured-results. Nguồn: ramp raw/aggregate |
| 9 | 106/106 regression; 18 run smoke/ramp/spike đạt; soak lỗi | Campaign failed, chưa đủ 24 run; chưa kết luận 10.000 VU. Nguồn: TRX, campaign status |
| 10 | Setup VPS đã có; chờ nghiệm thu DNS/TLS/browser/restore | Fixture restore 1,281 s cho 77.935 byte, chưa phải RTO. Nguồn: deployment-integration.json |
| 11 | Demo 10 phút và các gate chốt release | Soak, AI thật, VPS/restore, clean commit+digest, máy sạch+video. Offline fallback chỉ trình bày evidence. Nguồn: demo-script, release-checklist |

Nguồn chi tiết nằm trong speaker notes của từng slide; tài liệu gốc nằm trong thư mục backend của gói ZIP. Biểu đồ là native chart có thể chỉnh sửa, nhưng khi cập nhật dữ liệu phải chạy lại script tổng hợp và sửa cả báo cáo/outline cùng lúc.
