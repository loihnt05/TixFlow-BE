"""Create exportable figures from Summarize-Phase10.py output (requires matplotlib)."""
import json
import os
import sys
from pathlib import Path
root = Path(sys.argv[1]).resolve()
cache = root / ".matplotlib"
cache.mkdir(exist_ok=True)
os.environ.setdefault("MPLCONFIGDIR", str(cache))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

data = json.loads((root / "aggregate.json").read_text(encoding="utf-8"))
endpoints = ["browse", "detail", "availability", "hold", "checkout", "payment", "callback"]
colors = {"baseline": "#52677e", "optimized": "#008577"}
fig, axes = plt.subplots(1, 2, figsize=(13, 5.8))
for variant, offset in (("baseline", -.18), ("optimized", .18)):
    selected = {r["endpoint"]: r for r in data["endpoints"] if r["variant"] == variant}
    for ax, metric, title in zip(axes, ("p95_ms", "requests_per_second"), ("HTTP latency p95 (ms)", "Requests / second in shared measured window")):
        positions, values, low, high = [], [], [], []
        for i, endpoint in enumerate(endpoints):
            if endpoint not in selected:
                continue
            point = selected[endpoint][metric]
            positions.append(i + offset)
            values.append(point["median"])
            low.append(point["median"] - point["min"])
            high.append(point["max"] - point["median"])
        ax.bar(positions, values, width=.34, color=colors[variant], label=variant, yerr=[low, high], capsize=3)
        ax.set_xticks(range(len(endpoints)), endpoints, rotation=35, ha="right")
        ax.set_title(title, fontsize=11, loc="left")
        ax.spines[["top", "right"]].set_visible(False)
        ax.grid(axis="y", alpha=.18)
        ax.set_axisbelow(True)
axes[0].legend(frameon=False)
fig.suptitle("TixFlow phase 10 — local HTTP smoke comparison", fontsize=15, x=.07, ha="left")
fig.text(.07, .02, "Bars: median across runs. Whiskers: observed min–max, not confidence intervals.\nSingle API; k6 and services share Docker Desktop. Mixed workload; not production capacity.", fontsize=9, color="#48515c")
fig.tight_layout(rect=(.03, .11, 1, .94))
fig.savefig(root / "comparison.png", dpi=180)
fig.savefig(root / "comparison.svg")
plt.close(fig)
print(root / "comparison.png")
