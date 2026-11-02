# Phase 11 AI Booking Assistant

Ngày triển khai: 22/10/2026. Người dùng đã chọn OpenAI theo đề xuất phase 9. API/UI và adapter được triển khai; phần đánh giá chất lượng model thật cần API key và lượt chạy riêng. Không dùng kết quả fake transport để tuyên bố độ đúng ngôn ngữ tự nhiên của OpenAI.

## Luồng sử dụng

Customer mở `/assistant`, nhập nhu cầu, xem sự kiện/suất/loại vé/ghế. Assistant chỉ có bốn tool: `SearchEvents`, `GetEventDetails`, `GetAvailability`, `PrepareHold`. Lựa chọn được lấy giá từ API và hiển thị trong thẻ gồm tên event, session, thời gian, loại vé, số lượng, ghế, đơn giá, tổng tiền, currency và thời hạn hold. Chỉ nút **Xác nhận giữ vé** gửi request tới `/confirm-hold`. Sau đó `/checkout?...&holdId=...` đọc hold của người dùng và tiếp tục checkout/payment cũ, không tạo hold mới.

## Hợp đồng và biên quyền

| Endpoint | Quyền | Kết quả |
|---|---|---|
| `GET /api/v1/assistant/status` | Public | ready/fallback; không trả secret |
| `POST /api/v1/assistant/messages` | Customer | message, conversationToken, fallback, proposals, toolsUsed |
| `POST /api/v1/assistant/prepare-hold` | Customer | preview và confirmationToken; không giữ tồn |
| `POST /api/v1/assistant/confirm-hold` | Customer | `{ confirmationToken, confirmed: true }`, trả response của Booking API |

Message tối đa 2.000 ký tự. History tối đa ba lượt, tổng 6.000 ký tự; chỉ server tạo history. Token mã hóa Data Protection, tách purpose conversation/confirmation, gắn với `sub`. History hết hạn sau 30 phút; proposal sau hai phút. Người dùng khác, token sửa đổi, sai purpose hoặc hết hạn đều bị từ chối. Token xác nhận chỉ gửi UI, không đưa vào tool result/model. Người dùng có thể bỏ proposal hoặc bắt đầu hội thoại mới; không có DB mutation cần hoàn tác.

Agent không nhận DbContext, bearer token hay URL tùy ý. Typed gateway chỉ gọi các route catalog/availability cố định và Booking API. Khi xác nhận, gateway dùng token của chính Customer, Booking API kiểm tra lại role, trạng thái tài khoản, giá, quyền và inventory. Hold giữ idempotency key xác định từ nonce đã ký: retry cùng proposal trả cùng hold. `HoldQuote` được so sánh với tổng tiền, currency và thời hạn **trong transaction dưới khóa ticket type**; khác biệt trả `409 QUOTE_CHANGED`. Request booking cũ không truyền quote vẫn giữ hash/idempotency như trước.

Audit service riêng ghi `assistant.<ToolName>` trong `audit_logs`, actor, trace ID và outcome started/completed/failed. Không ghi prompt, argument, response, API key, JWT hay confirmation token. Audit started phải ghi thành công trước tool; không mở transaction DB trong lúc chờ OpenAI. Một response nghiệp vụ 409 từ Booking vẫn là completed của lần gọi API; xem Booking audit để phân biệt trạng thái nghiệp vụ.

## Microsoft Agent Framework và cấu hình

Spike/adapter chạy net8.0, khóa `Microsoft.Agents.AI.OpenAI 1.25.0`; dependencies được khóa trong `packages.lock.json` (OpenAI SDK 2.14.0). Responses SDK còn đánh dấu `OPENAI001`; suppression chỉ ở file adapter. Provider abstraction là `IAssistantProvider`; gateway, audit và tokens được tách để kiểm thử độc lập.

Adapter dùng Responses, `store=false`, không sử dụng conversation ID phía provider, tối đa 8 tool calls, output 1.200 tokens, deadline 30 giây và cancellation từ HTTP request. Các tool chạy tuần tự; catalog gửi model giới hạn 5 event, 10 session/detail, 20 loại vé và 40 ghế/type; trường `seatsTruncated` báo phần ghế bị rút gọn. UI hiển thị văn bản, không render HTML/link do model sinh. Timeout/provider error trả fallback rõ ràng và link catalog. Không có tool thanh toán/check-in/hủy/order lookup.

Trong `.env` backend (không commit):

```dotenv
ASSISTANT_PROVIDER=OpenAI
ASSISTANT_MODEL=gpt-5.4-mini
OPENAI_API_KEY=...
```

Docker Compose map các biến trên sang `Assistant__*`. Khi chạy `dotnet run`, dùng trực tiếp `Assistant__ApiKey`, `Assistant__Provider`, `Assistant__Model`. Không đưa key vào frontend hoặc `NEXT_PUBLIC_*`. Không có key thì chat dùng fallback; API booking vẫn hoạt động. `Assistant:BookingApiBaseUrl` mặc định `http://localhost:8080`; chỉ chấp nhận HTTPS hoặc loopback HTTP, không theo redirect. Khởi động stack như README; không reset volume.

Data Protection dùng volume `assistant_keys`. Các API replica cần chung key directory và application name `TixFlow.Assistant`; bảo vệ quyền đọc volume và backup. Rate limit Assistant riêng 10 request/phút/user, trả 429 + Retry-After; limiter hiện tại thuộc từng process, nhiều replica cần quota ở gateway nếu cần giới hạn tổng. Không mount key directory của Assistant cho Worker. Không rebuild stack thực nghiệm phase 10 để cập nhật Assistant.

## Kiểm chứng và phần nghiệm thu còn mở

```powershell
dotnet restore TixFlow.sln --locked-mode
dotnet test tests/TixFlow.Api.Tests --filter 'Category!=Baseline' --logger 'trx;LogFileName=regression.trx' --results-directory artifacts/phase11
# Frontend
npm run lint
npm run build
```

Testcontainers dùng PostgreSQL/RabbitMQ cách ly. Test phase 11 kiểm tra auth, explicit confirmation, tamper/expiry/owner/purpose, prepare không mutation, giá cũ bị chặn, concurrent confirmation replay, sai ID/ghế, provider timeout/error, hội thoại tách biệt và audit. Spike dùng Responses HTTP giả lập qua SDK và MAF thật để kiểm tra tool invocation, cấm CreateHold/PayOrder, `store=false` và token budget. Đây là kiểm tra ranh giới code khi model có hành vi không mong muốn, không phải phép đo khả năng chống prompt injection của model thật.

Tập 20 câu cố định ở `docs/evaluation/phase11-cases.json`. Khi API key đã được cấu hình và dữ liệu seed đang mở bán, đặt Customer JWT trong biến môi trường `TIXFLOW_EVAL_TOKEN`, chạy `python scripts/Evaluate-Phase11.py`. Script không gọi confirm/payment, không lưu JWT/history/confirmation tokens; ghi latency, fallback, tool usage, heuristic và câu trả lời cần chấm tay. Điền manualCorrectness sau khi đối chiếu catalog thực tế; không lấy tool selection heuristic làm độ chính xác tìm kiếm. Cần thêm lượt nhiều vòng với model thật, kiểm tra chi phí/quota thực tế và nghiệm thu browser đăng nhập trước khi đóng toàn bộ phase 11.

Nguồn kỹ thuật đã đối chiếu: [Microsoft Agent Framework OpenAI provider](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/openai), [GPT-5.4 mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini), [function calling](https://developers.openai.com/api/docs/guides/function-calling).
