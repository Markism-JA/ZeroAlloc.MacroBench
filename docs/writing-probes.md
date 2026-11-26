# Writing Probes

`ZeroAlloc.MacroBench` separates **workload authoring** from **orchestration, live logging, and artifact export**. Your probe class never prints to the console, never tracks GC deltas manually, and never writes JSON/CSV files.

---

## 1. Probe Lifecycle

Every probe implements either `IMacroProbe` (synchronous) or `IAsyncMacroProbe` (asynchronous `ValueTask`). `ProbeRunner` drives four phases in strict order:

1. `Setup(ctx)` / `SetupAsync(ctx)`: Allocate rings, open sockets/connections, spawn worker threads.
2. `Warmup(ctx)` / `WarmupAsync(ctx)`: Prime JIT, CPU caches, and queues for `ctx.WarmupDuration`.
   - Right after `Warmup` returns, `ProbeRunner` automatically wipes `ctx.Histogram`, zeroes all counters (`Operations`, `DroppedOperations`, `BytesTransferred`, `ScheduleSlips`, `CustomMetrics`), clears the Seqlock progress block, and runs a GC settle pass (if `CollectGcBeforeMeasure` is enabled).
3. `Measure(ctx)` / `MeasureAsync(ctx)`: Timed measurement window. `ProbeRunner` wraps this call with `NativeTsc.NowNs()`, `GcDeltaTracker` (`AllocatedBytes`, `Gen0`/`Gen1`/`Gen2`), and an optional background Seqlock live-progress timer.
4. `Teardown(ctx)` / `TeardownAsync(ctx)`: Always runs inside `finally`, even if `Measure` throws or a budget fails. Dispose workers, unmanaged buffers, and connections here.

---

## 2. Compile-Time Discovery, Parameter Sweeps & Budgets

Annotate any non-abstract probe class that has a public or internal parameterless constructor with `[MacroProbe]`. The bundled Roslyn incremental source generator emits `MacroBench.Generated.GeneratedProbes.CreateAll()` at compile time (zero reflection, NativeAOT-safe).

```csharp
using MacroBench.Attributes;
using MacroBench.Execution;
using MacroBench.Pacing;
using Platform.Time;

[MacroProbe("Ring.Transit")]
[ProbeBudget(MaxP99Ns = 10_000, MaxAllowedDrops = 0, MaxAllowedGcBytes = 0)]
public sealed class RingTransitProbe : IMacroProbe
{
    [ProbeParams(64, 256, 1024)]
    public int PayloadBytes { get; set; }

    [ProbeParams(100_000, 500_000)]
    public long TargetOpsPerSec { get; set; }

    public string Name => "Ring.Transit";

    public void Measure(ProbeContext ctx)
    {
        var pacer = OpenLoopPacer.CreatePaced(TargetOpsPerSec);
        long deadline = NativeTsc.NowNs() + (long)ctx.MeasureDuration.TotalNanoseconds;

        while (NativeTsc.NowNs() < deadline && !ctx.CancellationToken.IsCancellationRequested)
        {
            long scheduledNs = pacer.WaitNext();
            // ... dispatch & measure ...
            ctx.RecordLatencyNs(NativeTsc.NowNs() - scheduledNs);
            ctx.AddOperations(1);
            ctx.AddBytes(PayloadBytes);
        }

        ctx.SetScheduleSlips(pacer.ScheduleSlips);
    }
}
```

- Multiple `[ProbeParams]` properties or fields expand into a Cartesian scenario matrix (`PayloadBytes=64, TargetOpsPerSec=100000`, etc.).
- Invalid `[MacroProbe]` declarations (abstract class, missing `IMacroProbe`, or missing parameterless constructor) fail at compile time with diagnostic `MB001`.

---

## 3. Async Probes (`IAsyncMacroProbe`)

For probes that connect to external brokers or databases (e.g., NATS, PostgreSQL, TCP/gRPC), implement `IAsyncMacroProbe`. It extends `IMacroProbe`, so `[MacroProbe]`, `[ProbeParams]`, and `[ProbeBudget]` discover it automatically, and `ProbeRunner.RunAsync` / `ProbeSwitcher.RunAsync` await it natively without blocking.

```csharp
[MacroProbe("Broker.RoundTrip")]
public sealed class BrokerProbe : IAsyncMacroProbe
{
    public string Name => "Broker.RoundTrip";

    public async ValueTask SetupAsync(ProbeContext ctx)
    {
        // await client.ConnectAsync(ctx.CancellationToken);
        await Task.CompletedTask;
    }

    public async ValueTask MeasureAsync(ProbeContext ctx)
    {
        // await client.PublishAndWaitAsync(...);
        ctx.RecordLatencyNs(120_000);
        ctx.AddOperations(1);
        ctx.SetCustomMetric("BrokerReconnects", 0);
        await Task.CompletedTask;
    }

    public ValueTask TeardownAsync(ProbeContext ctx) => ValueTask.CompletedTask;
}
```

---

## 4. Recording Latencies from Unmanaged `delegate*` Callbacks

`ctx.HistogramHandle` is an 8-byte blittable `NativeHistogramHandle` wrapping a `NativeHistogramState*`. Pass it directly into unmanaged C-ABI structs or `delegate* unmanaged[Cdecl]` callbacks with zero `GCHandle` allocations:

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 64)]
private struct ReceiverState
{
    public NativeHistogramHandle Histogram;
    public long ReceivedCount;
}

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static unsafe void OnPacket(void* userCtx, long sentNs)
{
    var state = (ReceiverState*)userCtx;
    long elapsedNs = NativeTsc.NowNs() - sentNs;
    state->Histogram.Record(elapsedNs);
    state->ReceivedCount++;
}
```

### Multi-Worker Recording Without Atomic Contention

For multi-consumer probes, avoid `RecordLatencyNsAtomic` on hot loops. Give each worker its own `using var localHist = new NativeHistogram();` (~30.7 KB, L2-resident), record single-threaded with `localHist.Record(ns)`, and merge into `ctx.Histogram` once `Measure` finishes:

```csharp
ctx.Histogram.MergeFrom(worker1Hist);
ctx.Histogram.MergeFrom(worker2Hist);
```

---

## 5. Multi-Core Orchestration & Live Progress

- **`PinnedWorkerThread.Spawn(...)`:** Spawns a dedicated OS thread with an embedded `ThreadParker`. Defaults to `WorkerPinningMode.BestEffort` (pins on Linux/Windows, stays unpinned on macOS instead of throwing `PlatformNotSupportedException`). Pass `WorkerPinningMode.Strict` when hardware pinning is mandatory.
- **`PhaseBarrier`:** Cache-aligned barrier for `Ready -> Warmup -> Measure` rendezvous across N worker threads using progressive spin-wait followed by two-phase `ThreadParker` parking.
- **`ctx.PublishLiveProgress(ops, drops, bytes, slips)`:** Writes to a 64-byte-aligned Seqlock block (`LiveProgressBlock`). Call it periodically (e.g., every 4,096 ops) so `ProbeRunner`'s background observer timer can print live `[LIVE]` throughput updates without locking or stalling worker threads.
- **`ctx.SetCustomMetric("DemoteLatencyUs", value)`:** Attaches domain-specific numeric metrics (such as failover cutover time) that appear in Console, `summary.json`, `summary.md`, and `custom_metrics.csv`.
