# Demo 10 phút và phương án dự phòng

Đây là kịch bản diễn tập, chưa phải biên bản đã chạy browser/VPS. Dùng dữ liệu demo riêng; không trình chiếu `.env`, token, secret, QR còn giá trị sử dụng hoặc tài khoản thật.

## Chuẩn bị trước buổi demo

Khởi động backend/Worker/Keycloak/frontend theo README, chạy smoke, kiểm tra event đang mở bán và suất diễn còn tương lai. Chuẩn bị Customer cùng Organizer sở hữu event bằng hai browser profile để không nhầm role. Chọn loại vé còn đủ số lượng và một ghế để chứng minh xung đột. Không sửa ngày giờ hệ điều hành để ép seed còn hạn.

Mở sẵn catalog, account, Organizer check-in, report/biểu đồ và `offline-demo.html`. Với Assistant, nếu không có provider key thì giới thiệu fallback đúng phạm vi; không nhập câu rồi diễn như model thật đã trả lời. Với expiry mặc định 10 phút, chuẩn bị một hold sắp hết hạn trước giờ trình bày; nếu thay TTL cho demo, ghi cấu hình rõ và khởi động lại trước buổi demo, không sửa giữa bài.

## Lịch trình

| Thời gian | Thao tác/lời dẫn | Kết quả cần quan sát |
|---|---|---|
| 0:00–0:45 | Giới thiệu oversell, retry và payment/expiry race | Phạm vi payment mock và giới hạn local được nói rõ |
| 0:45–1:30 | Đăng nhập Customer, xem profile và event | Role đúng, catalog đọc API, không lộ token |
| 1:30–3:00 | Chọn vé, tạo hold, xem deadline/giá; dùng request replay chuẩn bị sẵn nếu cần | Cùng key trả cùng hold; tồn thay đổi đúng |
| 3:00–4:30 | Checkout, payment mock thành công, chờ vé ở account | Một order, trạng thái Paid; QR đến bất đồng bộ |
| 4:30–5:30 | Organizer check-in vé demo, quét lại | Lần hai không tạo check-in mới; role/ownership đúng |
| 5:30–6:30 | Hủy hold khác hoặc xem hold đã chuẩn bị hết hạn | Tồn được hoàn một lần; không chọn hold vừa thanh toán |
| 6:30–7:30 | Assistant tìm kiếm và xác nhận nếu provider sẵn sàng; nếu không, trình bày fallback | Proposal chưa giữ tồn; confirm mới gọi Booking |
| 7:30–9:15 | Hai biểu đồ phase 9/10 + regression | Nêu rõ TestServer/HTTP, số lần lặp, tail regression và soak lỗi |
| 9:15–10:00 | Giới thiệu setup VPS/backup và các gate mở | Không tuyên bố VPS đã deploy; chỉ rõ bước nghiệm thu tiếp theo |

## Nếu gặp lỗi

| Tình huống | Xử lý trên sân khấu | Điều không được suy diễn |
|---|---|---|
| Mất Internet | Stack local đã có image vẫn có thể chạy; nếu auth/provider cần mạng, chuyển offline-demo | Walkthrough evidence không chứng minh phiên UI vừa thành công |
| OpenAI timeout/quota | Chỉ rõ fallback, quay catalog và tiếp tục luồng đặt vé thủ công | Không giả kết quả AI hoặc dùng test transport để nói model đạt accuracy |
| Worker/queue trễ | Chờ/poll trạng thái đơn; giải thích outbox; chuyển sơ đồ nếu hết thời gian | Paid không đồng nghĩa ticket đã sẵn sàng ngay |
| 409 hết vé/quote đổi | Chọn lại theo dữ liệu mới; trình bày đây là xung đột được xử lý | Không bỏ qua kiểm tra để ép demo thành công |
| Stack lỗi | Mở fallback offline và chỉ nguồn raw/manifest | Không reset/delete volume trong buổi bảo vệ |

## Video cần quay sau nghiệm thu UI

Quay bản 8–12 phút theo lịch trên, hiển thị version/ngày ở đầu; che password/JWT và dữ liệu cá nhân. Ghi rõ môi trường local hay VPS, cấu hình AI enabled/fallback và payment mock. Xem lại toàn bộ video, xác nhận âm thanh/chữ đủ đọc, lưu SHA-256 cùng snapshot source tương ứng. Hiện chưa có video thao tác thực tế trong bộ evidence.

Đã có [video bằng chứng dự phòng](TixFlow-evidence-fallback.webm) khoảng 42 giây, không âm thanh, dựng từ bảy trang offline đã kiểm tra. Nhãn xuyên suốt video nói rõ đây là walkthrough evidence, không phải giao dịch ứng dụng/VPS. [Provenance](fallback-video.json) ghi hash video, hash HTML và ảnh nguồn; có thể dùng cùng thuyết minh trực tiếp khi stack không sẵn sàng.

## Câu hỏi dự kiến

- **Vì sao dùng PostgreSQL để chống oversell?** Conditional update, transaction và constraint kiểm tra cùng nguồn dữ liệu; cache không quyết định inventory.
- **Message gửi lặp có tạo thêm vé?** Inbox và uniqueness theo OrderItem/ordinal bảo vệ hiệu ứng; ACK sau commit. Không gọi delivery là exactly-once.
- **Tối ưu nhanh hơn bao nhiêu?** Trả lời theo từng workload và số lần lặp; đưa cả p95/p99 xấu đi trong HTTP, không dùng số TestServer làm SLA.
- **AI có tự thanh toán không?** Không có tool đó; giữ vé cũng cần confirmation rồi Booking kiểm tra lại.
- **Đã sẵn sàng production chưa?** Setup đã có; nghiệm thu VPS/restore/browser và các gate release vẫn mở.
