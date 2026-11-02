# Phase 10 — đang tiếp tục thực nghiệm

**Đối chiếu phase 13 ngày 24/10/2026:** chiến dịch `campaign-20261022T131923523Z` đã dừng với trạng thái `failed`, không còn đang chạy. Spike/smoke/ramp có 18 run đạt; soak baseline-1 lỗi `Load database SQL failed.`. Xem [bảng tổng hợp từ raw](../../release/measured-results.md) và [giới hạn kết luận](../../release/thesis-report.md). Nội dung ngày 22/10 bên dưới được giữ làm lịch sử; không phải trạng thái hiện tại.

Cập nhật 22/10/2026. **Chưa hoàn tất**: chiến dịch ngày 21/10 đạt 6 smoke và 6 ramp nhưng dừng ở spike (hai check lỗi, không có lỗi HTTP hoặc DB invariant). Đã sửa cuộc đua callback trong bộ đo; chiến dịch mới `campaign-20261022T131923523Z` chạy spike trước rồi smoke, ramp, soak trên protocol v3. Các bảng smoke đang có là số liệu v2 trước đó, chưa phải kết quả của chiến dịch mới.

Đã kiểm chứng:

- Candidate Checkout dùng khóa chia sẻ cho Event/Session chỉ đọc; Hold vẫn khóa độc quyền. Regression đạt 90/90. Test đối chứng trước sửa tái hiện chờ khóa hơn 10 giây, test lifecycle xác nhận vẫn chặn event đã hủy.
- Harness `http-load-v3` warm-up đầy đủ thanh toán và chạy Checkout trong workload ramp/spike/soak; failed payment giải phóng Hold. Callback k6 dùng event ID riêng, kiểm tra simulator xử lý trước và bắt buộc lần gửi lặp là Duplicate, trạng thái cuối phải đúng outcome. Raw lưu tên check.
- Monitoring Prometheus/Grafana thu HTTP, CPU/RAM, connections, lock wait, cache, outbox, queue và consumer lag. Snapshot chứa dữ liệu thật và được đọc lại qua API.
- Các image tham chiếu phase 8/9 được giữ nguyên; candidate được xác minh bằng manifest SHA-256 riêng.

Chiến dịch ngày 22/10 dùng smoke 10 VU mỗi scenario, ramp 20 VU, spike 100 VU, soak 10 VU giữ 30 phút; mỗi cấu hình lặp ba lần trên baseline và candidate. Chạy lại các profile với cùng bộ đo để giữ tính so sánh. Xem [runbook](../../phase-10-load-testing.md). `artifacts/phase10/campaign-*/status.json` là trạng thái đo, `resume-*/process.json` là PID và `exit.json` là kết quả tiến trình. Helper tự tổng hợp charts, snapshot và SLO local sau khi đủ 24 run đạt; báo cáo vẫn cần rà soát trước khi xác nhận hoàn tất.

[Nhật ký các lần kiểm tra và lỗi bộ đo](calibration.md).
