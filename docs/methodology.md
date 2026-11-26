# Macro-Benchmarking Methodology

Macro-benchmarks measure sustained, multi-threaded systems where OS scheduling, queueing theory, and cache locality dominate the results. This guide covers the main traps `ZeroAlloc.MacroBench` is designed to avoid.

---

## 1. Coordinated Omission & Open-Loop Pacing

A classic mistake in latency benchmarking is **closed-loop** timing: send a message, wait for a response, then send the next message (or stamp `NowNs()` *after* waiting for a rate limiter). When the system stalls for 10 ms, a closed-loop harness stops sending during the stall, so only 1 sample sees 10 ms instead of all the queued operations that should have arrived during that window. P99 looks deceptively clean while real users experience freezes.

`OpenLoopPacer` avoids coordinated omission in two ways:
1. It maintains an absolute schedule origin (`_startNs + _opIndex * _nsPerOp`) on `NativeTsc.NowNs()` with zero cumulative rounding drift.
2. `WaitNext()` returns the **scheduled** timestamp (`targetNs`), not post-spin wall time. When the consumer computes `NativeTsc.NowNs() - scheduledNs`, any stall or queue backlog is faithfully charged to every delayed operation.
3. If the producer itself falls more than one interval behind schedule, `pacer.ScheduleSlips` increments so you can tell whether the system under test lagged or the load generator was starved.

---

## 2. Pure Transit Floor (`Min`) vs. Queue Dwell (`P50` / `P99`)

Always read `Min` and `P50` together:
- **`Min` (hardware floor):** Measures a single operation traversing the pipeline when queues are empty (cache handoff, SHM pointer arithmetic, kernel loopback). If `Min` jumps, look for a regression in the hot path (e.g., an unintended `MemoryBarrier`, Nagle delay, or mutex contention).
- **`P50` / `P99` (queue dwell):** Reflects backlog accumulation inside intermediate rings when arrival rate approaches or exceeds drain rate. A healthy `Min` paired with a high `P50` means the consumer is falling behind the producer, not that the transport itself is slow.

---

## 3. Core Pinning, SMT Siblings & Thread Priority

- **Pin across physical cores, not SMT hyper-threads:** On 2-core / 4-thread CPUs, pinning a busy-spinning producer and consumer onto SMT siblings of the same physical core starves execution units and can starve background native threads (like GameNetworkingSockets' `ServiceThread`).
- **Prefer `ProcessPriorityClass.High` over `RealTime`:** `ProbeRunConfig.Default` elevates to `High` (not `RealTime`). On Linux/Windows, `RealTime` (`SCHED_FIFO`) on spinning worker threads can starve kernel softirqs and unpinned native I/O threads, producing worse tail latency than `High`.
- **macOS (`osx-arm64`):** XNU forbids hard CPU core pinning. `PinnedWorkerThread` defaults to `WorkerPinningMode.BestEffort` so probes run cleanly on macOS laptops while still enforcing hard pinning on Linux/Windows servers.

---

## 4. `NativeHistogram` vs. `UnmanagedLinearHistogram`

| Histogram | Footprint | Resolution | Range | Best For |
| :--- | :--- | :--- | :--- | :--- |
| `NativeHistogram` (default in `ProbeContext`) | ~30.7 KB (fits in L1/L2 cache) | Exact `1 ns` up to `255 ns`; `< 0.8%` relative bucket width above `256 ns` | `0 ns` to `~68.7 s` | Production probes, per-worker histograms (`MergeFrom`), native `delegate*` callbacks via `NativeHistogramHandle`. |
| `UnmanagedLinearHistogram` | `8 B * (maxTrackedNs + 1)` (~160 MB at 20 ms) | Exact `1 ns` everywhere | `0` to `maxTrackedNs` | Specialized lab runs needing exact 1 ns integer buckets over a bounded range. |

Both histograms compute `P50`, `P90`, `P95`, `P99`, `P99.9`, and `P99.99` in a single pass inside `Snapshot()`.
