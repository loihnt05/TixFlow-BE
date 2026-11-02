# Checklist trước release và bảo vệ

## Đã có bằng chứng

- [x] Regression hiện tại 106/106, TRX lưu trong gói.
- [x] Số liệu phase 9 tính từ JSON; phase 10 smoke/ramp/spike tổng hợp từ raw k6.
- [x] Giữ run soak lỗi và trạng thái campaign failed; không loại lỗi để tuyên bố đạt.
- [x] Bộ setup VPS và fixture migration/restore/restart được kiểm tra ở phase 12.
- [x] Báo cáo, sơ đồ, API, kịch bản và fallback offline có đường dẫn nguồn.
- [x] Rà soát 25/10: verifier có tám test đối chứng âm; số liệu native chart đối chiếu JSON đo; browser fallback được kiểm tra offline.
- [x] Có video bằng chứng dự phòng 42 giây, đã kiểm tra phát lại; không đánh dấu video giao dịch thực tế là đã hoàn thành.

## Chưa đủ điều kiện đóng release

| Gate | Điều kiện đóng | Bằng chứng cần lưu |
|---|---|---|
| G1 — Soak và độ trễ | Điều tra PostgreSQL dừng; chạy campaign so sánh hợp lệ; đánh giá p95/p99 hold/callback theo SLO đã chốt | raw, manifests, invariant, monitoring, status; không ghi đè run lỗi |
| G2 — AI thực tế | Chạy 20 case và nhiều lượt hội thoại bằng provider thật; chấm tay theo catalog; ghi chi phí/quota và fallback | kết quả evaluate đã loại token/prompt nhạy cảm, cách chấm, model/config |
| G3 — VPS | Người dùng điền SSH/domain/registry; HTTPS, login, hold/payment/expiry/ticket, restart/redeploy đạt | checklist ngày giờ, logs được lọc secret, image digest, browser evidence |
| G4 — Phục hồi | Restore DB + QR/assistant keys trên stack cách ly; login và đọc vé cũ đạt | thời gian toàn quy trình, tuổi backup, smoke; không gọi fixture 1,281 s là RTO |
| G5 — Bản phát hành | Review diff hai repo, commit sạch; CI pass; image digest đã publish khớp source; pin migration/seed | cặp SHA backend/frontend, digest API/Worker/frontend, migration checksums |
| G6 — Bàn giao | Người khác chạy trên máy sạch; walkthrough 8–12 phút và video ứng dụng thật | OS/runtime, lệnh, kết quả, video có ngày/version; che thông tin nhạy cảm |
| G7 — Hồ sơ trường | Bổ sung thông tin sinh viên/giảng viên, mẫu trường, trích dẫn học thuật và duyệt nội dung | báo cáo theo mẫu được người viết duyệt; bản thảo hiện tại chưa thay bản nộp |

Chỉ đánh dấu gate khi có đường dẫn evidence, ngày thực hiện và người kiểm tra. G3/G4 được hoãn theo yêu cầu chỉ setup VPS; không coi thời gian chờ hoặc đã có script là nghiệm thu đạt. Không tự commit/tag các thay đổi hiện tại trong quá trình đóng gói review.
