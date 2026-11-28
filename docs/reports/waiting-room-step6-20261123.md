# Waiting Room — đối chứng 3 cặp local (23/11/2026)

Validation dùng image `sha256:dade39449c960e3511a6508ac342fd526f594fa277e51b7098424810cffa6891`. [Audit JSON](../../artifacts/waiting-room/phase6-final-validation-audit.json) ghi `valid=true`, `sameInputs=true`, ba run mỗi mode. Manifest của từng run giữ revision `e55966bfe881252f9d196be6efeca56db03e4afe`, source/harness SHA-256, image ID, Compose và dataset. Source ở workspace đang dirty; image là snapshot build trước phép đo. Các run dùng một API 2 CPU/1 GiB, PostgreSQL 1 CPU/512 MiB, Redis AOF 0,5 CPU/256 MiB, cùng k6 130 VU; database/Redis được tạo mới cho từng run. Offered load: 100 Customer join trong một giây, 50 vé GeneralAdmission, `Active` tối đa 5 grant/giây.

| Run | Mode | status p95 | Hold p95/p99 | join→quyết định p95 | Paid→Ticket p95 |
| --- | --- | ---: | ---: | ---: | ---: |
| `20261123t031957838z` | Active | 2.876,5 ms | 3.895,2 / 5.362,7 ms | 19,43 s | 5.376,8 ms |
| `20261123t032321404z` | Disabled | — | 3.817,3 / 4.395,8 ms | 4,70 s | 6.179,3 ms |
| `20261123t032447812z` | Active | 594,3 ms | 2.210,4 / 3.147,3 ms | 20,25 s | 5.664,7 ms |
| `20261123t032621631z` | Disabled | — | 3.903,7 / 4.396,3 ms | 4,29 s | 4.886,0 ms |
| `20261123t032749435z` | Active | 576,0 ms | 3.515,1 / 5.412,5 ms | 19,92 s | 9.457,6 ms |
| `20261123t032929067z` | Disabled | — | 2.978,1 / 3.346,1 ms | 3,48 s | 3.683,7 ms |

Trung vị của ba p95 theo mode: Active status **594,3 ms**, Hold **3.515,1 ms**, quyết định **19,92 s**, Paid→Ticket **5.664,7 ms**; Disabled Hold **3.817,3 ms**, quyết định **4,29 s**, Paid→Ticket **4.886,0 ms**. Đây là trung vị của ba thống kê p95 riêng, không phải p95 gộp request. Disabled không gọi status nên ô này không có phép so sánh trực tiếp.

Mọi run: 100 join, 50 Hold, 50 Paid, 50 Ticket, 50 conflict `INSUFFICIENT_CAPACITY`, lỗi kỹ thuật 0, dropped iteration 0, check thất bại 0, invariant SQL sai 0, queue cuối run 0. Active cấp 100/100 lượt; grant cuối đến sau tối đa **22,28 / 21,12 / 22,80 s**. `join→terminal decision` max tương ứng **22,30 / 21,15 / 22,86 s**, nên không có khách thiếu quyết định tại 30 s. Admission không bảo đảm còn vé: một nửa khách được grant nhưng Booking trả conflict tồn kho.

So với ngưỡng đã khóa ở [hợp đồng bước 1](../virtual-waiting-room-phase-1.md): cổng tính đúng và lỗi kỹ thuật đạt; Active status p95 **không đạt 3/3**, Hold p95 **không đạt 2/3**, Hold p99 đạt 3/3, thời gian quyết định p95 đạt 3/3. Do đó validation hiệu năng tổng thể **không đạt**. Active trải thời gian chờ của khách ra khoảng 20 giây; dù một số run Hold nhanh hơn Disabled, số run ít và độ biến động lớn nên chưa kết luận cải thiện hiệu năng tổng thể.

Tập chẩn đoán trước đó (`phase6-validation-audit.json`) gọi status ngay sau join, khác frontend. Nó có cùng image riêng của tập chẩn đoán và đạt bất biến, nhưng không dùng để chấm protocol cuối. Profile cuối đợi `pollAfterMs` ngay lần đầu và ghi thêm Paid→Ticket; việc 100 khách có thể cùng poll sau 3 giây vẫn làm status chậm. Log API của ba run Active có status server-side p95 khoảng **2.754 / 415 / 570 ms**, nên run đầu có chậm đáng kể ngay trong API; chưa đo tách Redis, PostgreSQL và hàng đợi kết nối để quy nguyên nhân chính xác.

## Thử phân tán polling, sau đó bỏ thay đổi

Một pilot Active với `pollAfterMs` cố định theo `queueId` trong khoảng 3–5 giây đạt status p95 **172 ms** và Hold p95 **3.012 ms**. Pilot chỉ dùng để quyết định thử nghiệm. Tập validation khác gồm ba cặp cùng image `sha256:bb55f8e3876aa85dbf4815bca614afd50fdee52e7f1c1fd5d518423b01aee3b1` có audit `valid=true`, `sameInputs=true` tại `artifacts/waiting-room/phase6-tuned-validation-audit.json`. Active status p95 lần lượt **732 / 191 / 1.759 ms**; Hold p95 **4.103 / 2.115 / 3.239 ms**. Hai trong ba run vẫn vượt cả hai ngưỡng; trung vị status p95 **732 ms**, cao hơn tập trước. Bất biến vẫn đạt ở mọi run. Vì cải thiện không lặp lại ổn định, mã phân tán poll đã được bỏ; không dùng pilot đơn lẻ để tuyên bố đạt SLO.
