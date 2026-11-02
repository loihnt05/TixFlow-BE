# Baseline v1 — nghiệm thu giai đoạn 8

Ngày nghiệm thu: **18/10/2026**, múi giờ Asia/Saigon. Lệnh: `./scripts/Invoke-Phase8Baseline.ps1 -Mode All`.

Nguồn thực thi: `c07a603e8e925f0471c6a765bd14aeb3d1ab1321`, checkout sạch (`dirty=false`). Build Release: 0 warning, 0 error. Manifest lưu SHA-256 của 107 input; đối chiếu trực tiếp các input trong Docker image đã chạy không có hash lệch. Commit/tag đóng gói báo cáo chỉ thêm tài liệu và dữ liệu đo, không thay đổi code, cấu hình hoặc thuật toán đã đo.

Lưu ý checksum đa nền tảng: 7 `packages.lock.json` trong image/lúc thu manifest dùng CRLF; Git lưu LF và checkout sau đó đã chuẩn hóa về LF. Đã xác minh khác biệt chỉ ở ký tự xuống dòng, nội dung dependency không đổi. Khi so checksum ở checkout Linux hoặc sau chuẩn hóa Git cần xét CRLF/LF; không sửa manifest bằng chứng gốc.

FE được giữ nguyên tại `0c477bcde5e065ec66463c501830efebc95bc98a`; working tree FE sạch khi nghiệm thu. Không có thay đổi thiết kế hay mapping FE trong giai đoạn 8.

## Automated test report

**79 passed / 0 failed / 0 skipped**, thời lượng test do VSTest báo 3 phút 24 giây (không bao gồm build image). PostgreSQL/RabbitMQ được tạo mới và tự dọn. [TRX đầy đủ](tests.trx).

| Nhóm | Số test pass |
|---|---:|
| Authentication | 23 |
| Configuration | 4 |
| Catalog | 3 |
| Inventory | 2 |
| Hold concurrency/rollback | 5 |
| Checkout/payment | 3 |
| Worker/QR/check-in giai đoạn 7 | 14 |
| Domain rules: state, pricing, expiry, validation | 15 |
| Fault/validation giai đoạn 8 | 9 |
| Baseline concurrency (6 burst trong 1 test) | 1 |

Fault tests kiểm chứng callback trùng 20 lần chỉ settle một lần, hủy request chờ DB lock không để lại giao dịch dở, mất response sau commit replay đúng hold, TCP blackhole có timeout/retry, và kill consumer sau DB commit/trước ACK vẫn redeliver mà không nhân đôi vé. Sau crash, cả hai vé đều lấy QR thành công bằng API với khóa ký chung.

## Concurrency report

Mỗi burst bắt đầu từ cùng seed/reset: 50 vé × 250.000 VND, 1 vé/request, các customer/key khác nhau. Có 10 request warm-up trước lượt đo; 1 API TestServer, API pool=40, runner 2 CPU/2 GiB, PostgreSQL 1 CPU/512 MiB; cache off. [Điều kiện đầy đủ](../../phase-8-baseline.md), [manifest nguồn/môi trường](manifest.json), [JSON từng request và bằng chứng DB](concurrency.json).

| Lượt | Request | 201 Created | Hết vé 409 | Oversold | Request/s | p50 (s) | p95 (s) | p99 (s) |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 100 | 50 | 50 | 0 | 11.42 | 7.54 | 8.63 | 8.73 |
| 1 | 500 | 50 | 450 | 0 | 33.05 | 9.65 | 14.42 | 14.88 |
| 1 | 1000 | 50 | 950 | 0 | 33.07 | 17.81 | 28.45 | 29.42 |
| 2 | 100 | 50 | 50 | 0 | 18.41 | 4.06 | 5.27 | 5.37 |
| 2 | 500 | 50 | 450 | 0 | 33.03 | 9.46 | 14.58 | 14.99 |
| 2 | 1000 | 50 | 950 | 0 | 29.10 | 17.10 | 33.05 | 33.96 |

Tổng 3.200 request đo: 300 Created và 2.900 `INSUFFICIENT_CAPACITY`; không có 5xx, timeout hay `REQUEST_IN_PROGRESS`. Sau **mỗi** burst: held=50, sold=0, tổng quantity của active hold items=50, idempotency hoàn tất=N. Số lượng kết quả trùng khớp giữa hai lượt; latency có dao động, không được xem là một hằng số tái lập chính xác.

## Bottleneck có bằng chứng

`pg_stat_activity` được lấy mẫu mỗi 25ms bằng pool observer riêng. Trong cả sáu burst, **100% tổng số waiter thuộc nhóm Lock được quan sát đang chờ câu `SELECT ... FROM events ... FOR UPDATE`**; số waiter đồng thời cao nhất là **39**, trong khi pool API có 40 slot. Đây là tỷ lệ trong các mẫu waiter, không phải 100% thời gian request hay CPU.

`pg_stat_statements` xếp câu khóa event ở vị trí cao nhất theo tổng execution time. Mean execution time của nó lần lượt là 1.902,26 / 963,29 / 989,80 / 1.599,97 / 964,49 / 1.156,96 ms theo thứ tự sáu dòng bảng. Tổng thời gian SQL có chờ khóa chồng lấp giữa các connection, không được cộng và coi là thời gian CPU hay wall clock của burst.

`EXPLAIN (ANALYZE, BUFFERS)` của inventory lookup sau burst mất khoảng **0,07–0,13 ms**, trả 1 row, không đọc disk block. Plan dùng Seq Scan trên bảng inventory nhỏ (13 row trong fixture), không có bằng chứng scan lớn là nguyên nhân ở dataset này. Plan được lấy khi burst đã kết thúc nên không đo lock contention trong burst.

Kết hợp ba nguồn bằng chứng, điểm nghẽn chính được xác định là **khóa event quá rộng**, tuần tự hóa các giao dịch của cùng event và khiến các request phía sau xếp hàng cả tại pool. Chưa thay đổi lock hierarchy/thuật toán để giữ nguyên baseline; tối ưu ở giai đoạn sau phải dùng version mới và đo lại cùng harness. Không suy rộng kết luận về I/O cho dataset lớn hơn khi chưa đo.

## Giới hạn và tái tạo

TestServer chạy HTTP pipeline/JWT/backend thật và PostgreSQL thật; phép đo **không bao gồm Kestrel, TLS, OIDC discovery, network HTTP hay trình duyệt**. Load generator dùng chung quota với API. Kết quả này là mốc tính đúng và contention trong điều kiện đã ghi, không phải throughput production hoặc end-to-end. Broker crash/timeout được kiểm tra riêng, không nằm trong burst chỉ giữ vé.

Từ tag `baseline-v1-phase8`, chạy lại lệnh đầu báo cáo. Mỗi lần tạo thư mục timestamp mới trong `artifacts/phase8/`; không ghi đè bằng chứng này. Mọi thay đổi code thuật toán, seed, pool hoặc tài nguyên cần baseline version mới và changelog. Không dùng các lượt thử lỗi/bị ngắt ngày 15/10 làm kết quả chính thức.
