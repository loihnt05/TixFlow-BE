# Phase 10 HTTP load results

Completed runs: 6.

Each endpoint rate uses the complete measured workload window (first to last custom HTTP sample), excluding setup. Concurrent scenarios share CPU and database locks; this is a mixed-workload experiment, not isolated endpoint capacity.

| Variant | Endpoint | Runs | Median p50 ms | Median p95 ms | p95 min–max ms | Median p99 ms | Median req/s |
|---|---|---:|---:|---:|---:|---:|---:|
| baseline | availability | 3 | 198.00 | 958.98 | 842.20–1292.43 | 1228.79 | 11.55 |
| baseline | browse | 3 | 148.30 | 1260.19 | 1236.21–1297.95 | 1454.81 | 11.55 |
| baseline | callback | 3 | 167.62 | 578.71 | 453.89–1173.87 | 612.35 | 4.62 |
| baseline | checkout | 3 | 672.84 | 870.83 | 752.12–3479.76 | 2170.11 | 2.31 |
| baseline | checkout_replay | 3 | 6.52 | 10.83 | 9.84–49.09 | 19.24 | 2.31 |
| baseline | detail | 3 | 109.44 | 299.83 | 287.83–492.50 | 406.93 | 11.55 |
| baseline | hold | 3 | 456.55 | 4437.48 | 4427.46–6185.01 | 4690.94 | 25.42 |
| baseline | hold_replay | 3 | 9.20 | 1407.39 | 825.22–1440.29 | 2674.15 | 8.20 |
| baseline | payment | 3 | 24.91 | 107.59 | 80.61–133.41 | 137.22 | 2.31 |
| baseline | payment_replay | 3 | 8.63 | 27.20 | 19.65–31.07 | 33.30 | 2.31 |
| baseline | payment_state | 3 | 8.31 | 23.40 | 12.70–36.87 | 28.53 | 2.31 |
| optimized | availability | 3 | 280.57 | 1080.54 | 1010.33–4118.68 | 1486.34 | 12.76 |
| optimized | browse | 3 | 101.98 | 1776.68 | 1291.50–3464.20 | 1780.19 | 12.76 |
| optimized | callback | 3 | 118.69 | 473.46 | 346.15–1014.42 | 527.84 | 5.10 |
| optimized | checkout | 3 | 1424.43 | 3714.84 | 3338.45–9117.23 | 4176.79 | 2.55 |
| optimized | checkout_replay | 3 | 8.30 | 33.52 | 18.78–53.68 | 40.98 | 2.55 |
| optimized | detail | 3 | 106.87 | 495.49 | 485.31–3212.41 | 682.65 | 12.76 |
| optimized | hold | 3 | 292.88 | 3092.89 | 2806.89–10537.82 | 4270.74 | 28.07 |
| optimized | hold_replay | 3 | 50.79 | 1746.73 | 1067.34–2744.76 | 2205.87 | 9.06 |
| optimized | payment | 3 | 36.30 | 98.44 | 72.64–161.35 | 102.93 | 2.55 |
| optimized | payment_replay | 3 | 8.56 | 35.65 | 18.99–66.93 | 40.35 | 2.55 |
| optimized | payment_state | 3 | 8.42 | 35.35 | 15.69–130.56 | 38.25 | 2.55 |

All included runs passed k6 thresholds and database invariants. Zero invariant violations is required but does not establish production capacity. See manifests, raw points, resource/database samples and the phase 10 runbook for limitations.
