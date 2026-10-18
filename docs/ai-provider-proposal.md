# Đề xuất provider/model AI cho TixFlow

Ngày đối chiếu: 18/10/2026. Trạng thái: **đề xuất, chưa có quyết định lựa chọn của người dùng**. Không thêm API key, SDK hoặc gọi dịch vụ trả phí trong giai đoạn 9.

Khuyến nghị bắt đầu với **OpenAI Responses API, model `gpt-5.4-mini`**, cấu hình provider/model phía backend để thay được. Phạm vi ban đầu: tìm sự kiện bằng ngôn ngữ tự nhiên, hỏi đáp dựa trên dữ liệu catalog/chính sách và hướng dẫn mua vé. Không cần flagship cho mọi lượt chat. Model hỗ trợ function calling/structured outputs; cần đánh giá tiếng Việt trên tình huống TixFlow trước khi chốt chất lượng. [Model chính thức](https://developers.openai.com/api/docs/models/gpt-5.4-mini).

Giá niêm yết tại thời điểm xem: 0,75 USD/triệu token input, 4,50 USD/triệu token output. Ví dụ 1.000 lượt × (2.000 input + 500 output) = 3,75 USD, chưa tính lượt model bổ sung, reasoning tokens tính vào output khi có, phí công cụ hoặc thuế. Đây là phép tính theo giả định, không phải dự báo hóa đơn. Đặt ngân sách và giới hạn output trước thử nghiệm.

Backend cung cấp tool có schema cụ thể như searchEvents/getAvailability/getOrder của chính người dùng. Giá/tồn kho phải lấy từ API; không cho model tự suy diễn. Việc giữ vé, thanh toán, hủy hoặc check-in vẫn qua API nghiệp vụ, kiểm tra quyền và idempotency; mutation cần xác nhận người dùng theo thiết kế Assistant hiện có. Không gửi QR token, credentials hoặc thông tin thanh toán cho model. Chỉ gửi dữ liệu cần thiết cho câu hỏi.

Đề nghị làm tập 50–100 hội thoại tiếng Việt (địa điểm/ngày tương đối/giá/vé hết/chính sách/đổi ý), đo tool-call đúng, câu trả lời có căn cứ, p95 và chi phí/lượt. Nếu mini không đạt ngưỡng sẽ so sánh model mạnh hơn hoặc provider khác trên cùng tập; chưa có dữ liệu để khẳng định provider nào tốt nhất cho TixFlow. Phần AI thuộc sprint sau; không để việc chưa chọn tài khoản/API key chặn tối ưu backend hiện tại.
