# Phase 10 HTTP load results

Completed runs: 6.

Each endpoint rate uses the complete measured workload window (first to last custom HTTP sample), excluding setup. Concurrent scenarios share CPU and database locks; this is a mixed-workload experiment, not isolated endpoint capacity.

| Variant | Endpoint | Runs | Median p50 ms | Median p95 ms | p95 min–max ms | Median p99 ms | Median req/s |
|---|---|---:|---:|---:|---:|---:|---:|
| baseline | availability | 3 | 402.11 | 2488.11 | 1123.99–2500.85 | 2983.53 | 7.43 |
| baseline | browse | 3 | 211.62 | 2214.66 | 896.40–2796.90 | 3304.74 | 7.43 |
| baseline | callback | 3 | 161.67 | 832.94 | 514.25–862.22 | 1097.02 | 2.97 |
| baseline | cancel | 3 | 33.66 | 88.07 | 66.02–156.03 | 94.01 | 0.74 |
| baseline | checkout | 3 | 589.84 | 1795.92 | 1275.76–2501.51 | 2828.44 | 1.49 |
| baseline | checkout_replay | 3 | 7.77 | 26.28 | 8.45–33.84 | 46.48 | 1.49 |
| baseline | detail | 3 | 202.59 | 680.34 | 503.43–895.09 | 1119.55 | 7.43 |
| baseline | hold | 3 | 585.80 | 8947.26 | 4982.56–10218.14 | 9386.38 | 16.36 |
| baseline | hold_replay | 3 | 12.35 | 1087.95 | 551.22–1230.64 | 3335.68 | 5.28 |
| baseline | payment | 3 | 26.48 | 83.84 | 48.71–94.03 | 159.33 | 1.49 |
| baseline | payment_replay | 3 | 9.29 | 40.92 | 9.99–88.51 | 50.43 | 1.49 |
| baseline | payment_state | 3 | 7.86 | 34.63 | 11.87–53.55 | 40.42 | 1.49 |
| optimized | availability | 3 | 415.52 | 1711.64 | 699.21–2005.11 | 2319.20 | 6.56 |
| optimized | browse | 3 | 283.50 | 1080.18 | 389.34–1110.40 | 1504.74 | 6.56 |
| optimized | callback | 3 | 185.03 | 3742.04 | 1419.12–4490.96 | 4308.15 | 2.62 |
| optimized | cancel | 3 | 89.06 | 453.66 | 78.02–512.50 | 631.66 | 0.66 |
| optimized | checkout | 3 | 586.88 | 1960.67 | 528.88–4249.78 | 2952.40 | 1.31 |
| optimized | checkout_replay | 3 | 90.50 | 194.30 | 114.97–198.91 | 201.25 | 1.31 |
| optimized | detail | 3 | 316.64 | 986.65 | 602.87–1494.00 | 1204.44 | 6.56 |
| optimized | hold | 3 | 499.18 | 5336.27 | 1927.87–7011.45 | 7862.34 | 14.43 |
| optimized | hold_replay | 3 | 97.83 | 1748.77 | 1010.97–2291.18 | 4382.65 | 4.66 |
| optimized | payment | 3 | 200.15 | 584.47 | 203.18–644.52 | 595.25 | 1.31 |
| optimized | payment_replay | 3 | 81.85 | 112.32 | 98.09–124.63 | 123.16 | 1.31 |
| optimized | payment_state | 3 | 23.33 | 75.31 | 26.96–90.95 | 77.37 | 1.31 |

All included runs passed k6 thresholds and database invariants. Zero invariant violations is required but does not establish production capacity. See manifests, raw points, resource/database samples and the phase 10 runbook for limitations.
