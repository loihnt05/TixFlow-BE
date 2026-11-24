# Virtual Waiting Room — bước 6: đối chứng tải và OpenAPI runtime

Ngày 23/11/2026. Bước này đo ba cặp `Active`/`Disabled` bằng **cùng một image, cùng hash source/harness, một API 2 CPU/1 GiB, PostgreSQL 1 CPU/512 MiB, Redis 0,5 CPU/256 MiB**, 100 Customer bắt đầu join trong một giây và 50 vé GeneralAdmission. Mỗi run có Compose project, database và Redis riêng. Profile Active đợi `pollAfterMs` trước lần status đầu, giống client checkout; các lần sau dùng 3–5 giây và jitter 0–500 ms.

Backend regression đạt **137/137** (`dotnet test TixFlow.sln -c Release --no-build`); test mới xác minh OpenAPI runtime.

## Kết quả validation

[Báo cáo từng run](reports/waiting-room-step6-20261123.md) và audit máy đọc được ở `artifacts/waiting-room/phase6-final-validation-audit.json`. Cả sáu run qua cổng tính đúng: mỗi run 100 join, 50 Hold/Paid/Ticket, 50 conflict hết vé, không lỗi kỹ thuật, drop, oversell, duplicate hoặc thiếu vé sau Paid; hàng chờ còn 0. Active cấp đúng 100 lượt; thời gian chờ grant dài nhất 21,1–22,8 giây.

**Ngưỡng độ trễ local chưa đạt.** `GET status` p95 là 2.876,5 / 594,3 / 576,0 ms, đều vượt 500 ms. `POST holds` p95 sau Gate là 3.895,2 / 2.210,4 / 3.515,1 ms, hai run vượt 3.000 ms; p99 đều dưới 6.000 ms. `join→terminal decision` p95 đều dưới 30 giây và không có khách chưa có quyết định tại mốc 30 giây. Chưa coi Waiting Room sẵn sàng bật cho khách thật chỉ vì cổng tính đúng xanh.

Tập sáu run trước khi sửa nhịp poll được giữ làm **chẩn đoán**, không trộn với validation. Run Active đầu của tập mới có độ trễ cao hơn các run sau; không bỏ run này vì nó hợp lệ theo protocol. Chưa đổi budget controller hoặc hạ ngưỡng sau khi xem kết quả. Bước tối ưu tiếp theo cần đo riêng thời gian Redis, PostgreSQL và hàng đợi request của status/Hold, rồi chạy tập validation độc lập trên image mới.

Đã thử phân tán `pollAfterMs` ổn định theo `queueId` trong khoảng 3–5 giây. Pilot Active có status p95 172 ms, nhưng tập validation mới cùng image vẫn vượt ngưỡng status và Hold ở **2/3 run**; trung vị status p95 732 ms. Thay đổi này đã được bỏ, còn raw/audit tại `artifacts/waiting-room/phase6-tuned-validation-audit.json` để điều tra. Runtime hiện vẫn trả 3.000 ms như trước bước 6.

## OpenAPI

Runtime Swagger Development hiện mô tả response `200/400/404/409/503` của join, `200/400/404/503` của status, `204/400/404/503` của abandon, cùng `Idempotency-Key` và `X-Admission-Token` điều kiện trên `POST /holds`. [OpenAPI JSON](reports/waiting-room-openapi.json) và [provenance](reports/waiting-room-openapi-provenance.json) được xuất từ assembly Release bằng `./scripts/Export-WaitingRoomOpenApi.ps1`, không gọi nghiệp vụ vào database fixture. Test tích hợp kiểm tra các path, schema, status và header này. Policy quản trị/audit vẫn chưa có route production.

## Chạy lại

```powershell
./scripts/Invoke-WaitingRoomLoad.ps1 -Mode Active -Arrivals 100 -GrantsPerSecond 5 -ApiReplicas 1
# Lấy tag image từ manifest run đầu; dùng -CandidateImage <tag> cho năm run còn lại.
./scripts/Invoke-WaitingRoomLoad.ps1 -Mode Disabled -Arrivals 100 -GrantsPerSecond 5 -ApiReplicas 1 -CandidateImage <tag>
```

Chạy ba run mỗi mode; đưa đúng sáu thư mục run cho `node scripts/Summarize-WaitingRoomValidation.mjs ... --output artifacts/waiting-room/audit.json`. Audit yêu cầu image ID, revision, hash, dataset, workload và tài nguyên khớp nhau. Raw k6, summary, invariant, manifest, Compose đã render và log của từng run nằm trong `artifacts/waiting-room/<run-id>-<mode>/`.

## Giới hạn

Đây là phép đo local trên một máy, không phải SLA production. Chưa đo soak dài, network partition, Redis kill đột ngột, hoặc hệ nhiều máy. p95 Paid→Ticket đã được ghi nhưng chưa có ngưỡng số khóa trước validation. API quản trị policy với audit và quy trình bật theo sự kiện còn thiếu.
