# Số liệu đã đối chiếu

Sinh từ raw bằng `scripts/Build-Phase13Evidence.py`. Median của các lần chạy; không gộp percentile giữa các workload.

## Phase 9 — TestServer, hai lần lặp

| Requests/burst | Baseline req/s | Optimized req/s | Baseline p95 ms | Optimized p95 ms |
|---:|---:|---:|---:|---:|
| 100 | 15.75 | 37.11 | 6236.75 | 3325.88 |
| 500 | 33.07 | 66.66 | 14440.19 | 7381.66 |
| 1000 | 35.33 | 76.38 | 27444.87 | 12653.22 |

## Phase 10 — spike

6/6 run quan sát đạt; complete=True. Nguồn: `artifacts/phase10/20261022T131923730Z-spike`.

| Variant | Endpoint | Runs | p50 ms | p95 ms | p99 ms | req/s |
|---|---|---:|---:|---:|---:|---:|
| baseline | availability | 3 | 204.87 | 791.81 | 902.48 | 13.68 |
| baseline | browse | 3 | 396.79 | 1571.76 | 2069.39 | 13.68 |
| baseline | callback | 3 | 238.66 | 1889.05 | 2105.47 | 5.49 |
| baseline | cancel | 3 | 684.39 | 1091.43 | 1331.83 | 12.31 |
| baseline | checkout | 3 | 2564.76 | 3194.41 | 3366.82 | 2.74 |
| baseline | checkout_replay | 3 | 586.73 | 960.60 | 1058.69 | 2.74 |
| baseline | detail | 3 | 214.39 | 997.02 | 1196.87 | 13.68 |
| baseline | hold | 3 | 2015.87 | 2945.42 | 3997.88 | 13.68 |
| baseline | hold_replay | 3 | 499.69 | 809.87 | 957.72 | 13.68 |
| baseline | payment | 3 | 816.14 | 1220.64 | 1390.92 | 2.74 |
| baseline | payment_replay | 3 | 710.93 | 1094.70 | 1186.85 | 2.74 |
| baseline | payment_state | 3 | 602.69 | 908.79 | 1008.95 | 2.74 |
| optimized | availability | 3 | 113.63 | 396.27 | 588.44 | 20.06 |
| optimized | browse | 3 | 204.07 | 393.84 | 484.23 | 20.06 |
| optimized | callback | 3 | 192.01 | 4548.13 | 6633.98 | 8.04 |
| optimized | cancel | 3 | 500.49 | 1198.69 | 2315.69 | 18.04 |
| optimized | checkout | 3 | 714.36 | 3388.81 | 6142.33 | 4.02 |
| optimized | checkout_replay | 3 | 410.12 | 593.46 | 779.79 | 4.02 |
| optimized | detail | 3 | 313.24 | 593.41 | 801.95 | 20.06 |
| optimized | hold | 3 | 500.93 | 3797.42 | 5430.99 | 20.06 |
| optimized | hold_replay | 3 | 310.54 | 592.62 | 725.80 | 20.06 |
| optimized | payment | 3 | 525.09 | 787.35 | 905.05 | 4.02 |
| optimized | payment_replay | 3 | 399.34 | 594.61 | 717.69 | 4.02 |
| optimized | payment_state | 3 | 397.00 | 583.76 | 720.82 | 4.02 |

## Phase 10 — smoke

6/6 run quan sát đạt; complete=True. Nguồn: `artifacts/phase10/20261022T133115880Z-smoke`.

| Variant | Endpoint | Runs | p50 ms | p95 ms | p99 ms | req/s |
|---|---|---:|---:|---:|---:|---:|
| baseline | availability | 3 | 198.11 | 838.59 | 1110.46 | 12.03 |
| baseline | browse | 3 | 198.64 | 1181.56 | 1227.22 | 12.03 |
| baseline | callback | 3 | 206.86 | 611.49 | 622.43 | 4.81 |
| baseline | cancel | 3 | 26.64 | 59.57 | 63.30 | 1.20 |
| baseline | checkout | 3 | 526.32 | 1445.81 | 3015.09 | 2.41 |
| baseline | checkout_replay | 3 | 6.37 | 7.58 | 9.60 | 2.41 |
| baseline | detail | 3 | 194.94 | 295.75 | 390.68 | 12.03 |
| baseline | hold | 3 | 486.31 | 4898.18 | 5159.60 | 26.46 |
| baseline | hold_replay | 3 | 8.77 | 1208.40 | 1826.76 | 8.54 |
| baseline | payment | 3 | 19.07 | 59.27 | 60.24 | 2.41 |
| baseline | payment_replay | 3 | 6.53 | 12.31 | 38.66 | 2.41 |
| baseline | payment_state | 3 | 7.41 | 42.43 | 44.58 | 2.41 |
| optimized | availability | 3 | 192.65 | 747.11 | 1193.34 | 17.12 |
| optimized | browse | 3 | 101.52 | 447.22 | 608.40 | 17.12 |
| optimized | callback | 3 | 67.79 | 1406.03 | 1588.17 | 6.85 |
| optimized | cancel | 3 | 26.05 | 85.83 | 94.71 | 1.71 |
| optimized | checkout | 3 | 192.82 | 611.21 | 683.95 | 3.42 |
| optimized | checkout_replay | 3 | 44.04 | 99.69 | 111.59 | 3.42 |
| optimized | detail | 3 | 110.84 | 585.70 | 750.39 | 17.12 |
| optimized | hold | 3 | 192.40 | 2449.08 | 3387.74 | 37.67 |
| optimized | hold_replay | 3 | 56.54 | 617.82 | 1624.08 | 12.16 |
| optimized | payment | 3 | 95.67 | 209.29 | 212.72 | 3.42 |
| optimized | payment_replay | 3 | 60.61 | 95.01 | 104.11 | 3.42 |
| optimized | payment_state | 3 | 6.69 | 37.62 | 38.06 | 3.42 |

## Phase 10 — ramp

6/6 run quan sát đạt; complete=True. Nguồn: `artifacts/phase10/20261022T133833106Z-ramp`.

| Variant | Endpoint | Runs | p50 ms | p95 ms | p99 ms | req/s |
|---|---|---:|---:|---:|---:|---:|
| baseline | availability | 3 | 10.23 | 82.65 | 92.33 | 16.34 |
| baseline | browse | 3 | 13.31 | 82.39 | 93.44 | 16.34 |
| baseline | callback | 3 | 59.87 | 715.50 | 887.83 | 6.54 |
| baseline | cancel | 3 | 75.02 | 108.80 | 185.53 | 14.71 |
| baseline | checkout | 3 | 553.88 | 814.82 | 992.54 | 3.27 |
| baseline | checkout_replay | 3 | 5.81 | 65.06 | 76.98 | 3.27 |
| baseline | detail | 3 | 13.70 | 84.29 | 96.29 | 16.34 |
| baseline | hold | 3 | 566.83 | 811.21 | 1001.75 | 16.34 |
| baseline | hold_replay | 3 | 7.24 | 73.58 | 83.42 | 16.34 |
| baseline | payment | 3 | 18.47 | 88.05 | 100.85 | 3.27 |
| baseline | payment_replay | 3 | 5.90 | 66.90 | 77.72 | 3.27 |
| baseline | payment_state | 3 | 6.76 | 70.71 | 80.84 | 3.27 |
| optimized | availability | 3 | 10.39 | 75.11 | 90.53 | 21.89 |
| optimized | browse | 3 | 3.50 | 58.54 | 73.19 | 21.89 |
| optimized | callback | 3 | 54.97 | 1692.33 | 2386.00 | 8.76 |
| optimized | cancel | 3 | 91.46 | 292.57 | 505.52 | 19.70 |
| optimized | checkout | 3 | 66.02 | 584.83 | 1365.36 | 4.38 |
| optimized | checkout_replay | 3 | 5.53 | 64.14 | 79.81 | 4.38 |
| optimized | detail | 3 | 4.40 | 58.74 | 73.45 | 21.89 |
| optimized | hold | 3 | 135.49 | 1090.09 | 1887.85 | 21.89 |
| optimized | hold_replay | 3 | 6.89 | 70.47 | 82.80 | 21.89 |
| optimized | payment | 3 | 19.27 | 89.52 | 104.59 | 4.38 |
| optimized | payment_replay | 3 | 5.70 | 67.15 | 82.48 | 4.38 |
| optimized | payment_state | 3 | 6.69 | 67.71 | 79.94 | 4.38 |

## Phase 10 — soak

0/1 run quan sát đạt; complete=False. Nguồn: `artifacts/phase10/20261022T140340222Z-soak`.

Không đưa run lỗi vào so sánh đạt. Chiến dịch vẫn failed; chưa có kết quả soak hoàn chỉnh.
