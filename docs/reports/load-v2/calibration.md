# Nhật ký kiểm tra bộ đo trước chiến dịch


| Lần chạy | Kết quả | Diễn giải |
|---|---|---|
| 20261020T123316578Z-smoke | Lỗi khởi động monitoring | Prometheus thiếu quyền ghi tmpfs; chưa đo workload |
| 20261020T123518994Z-smoke | Nghiệp vụ đạt, monitoring lỗi | Collector chưa xử lý đúng mảng queue RabbitMQ; không dùng để so sánh |
| 20261020T123714533Z-smoke | Optimized smoke đạt | 1 lần để xác minh bộ đo; chưa đủ ba lần cho phép so |
| 20261020T123925093Z-smoke | Dừng ở optimized-1 | Docker stats EOF khi k6 tự xóa; thiếu DB invariant cuối cho run này, không kết luận tính đúng và không gộp vào phép so |

Raw của cả các lần lỗi vẫn được giữ trong `artifacts/phase10`. Không loại lỗi âm thầm để chọn kết quả đẹp. Báo cáo này sẽ được thay bằng bảng/charts và đề xuất SLO local sau khi toàn bộ chiến dịch đạt cổng đo; đề xuất SLO không phải SLA production hay kết quả kiểm chứng độc lập.

Ngày 22/10: campaign-20261021T122259071Z đã hoàn thành 6 smoke + 6 ramp, nhưng dừng ở baseline-1 của 20261021T130020824Z-spike: k6 exit 99, hai check lỗi, technical_errors=0, tất cả DB invariant=0. Protocol v2 không lưu tên check. Rà soát phát hiện callback của k6 tái sử dụng event ID của simulator dù timestamp/failureReason khác; API so toàn bộ payload và có thể trả WEBHOOK_EVENT_CONFLICT hợp lệ. V3 dùng event ID riêng, kiểm tra trường hợp simulator thắng bằng PAYMENT_NOT_PENDING ở lần gửi đầu và xác minh outcome cuối; thêm tên check vào raw metrics. Các kết quả cũ giữ nguyên, chiến dịch mới chạy lại đủ bốn profile cùng protocol để tránh gộp bộ đo khác nhau. Đây là chẩn đoán từ source; raw v2 không chứa response body để quy chính xác hai check lỗi cho callback.
