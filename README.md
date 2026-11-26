# ZeroAlloc.MacroBench

[![CI](https://github.com/Markism-JA/ZeroAlloc.MacroBench/actions/workflows/ci.yml/badge.svg)](https://github.com/Markism-JA/ZeroAlloc.MacroBench/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.MacroBench.svg)](https://www.nuget.org/packages/ZeroAlloc.MacroBench)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)

Low-level macro-benchmarking and performance-regression harness for stateful, multi-threaded .NET daemons and pipelines (64-bit AnyCPU).

Built on top of [`ZeroAlloc.Platform`](https://www.nuget.org/packages/ZeroAlloc.Platform), tested in CI across 64-bit `x64` and `ARM64` (`linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`).

---

## Origin & Intent

BenchmarkDotNet excels at isolated microbenchmarks, and HTTP load testers (k6, NBomber) target millisecond network APIs. Neither fits stateful, core-pinned, multi-threaded .NET systems — IPC rings, game server loops, dataplanes, or lock-free daemons — where you need to drive sustained load for seconds and measure real tail latency, drops, and zero-allocation invariants.

`ZeroAlloc.MacroBench` was extracted from the macro-benchmarking harnesses built for *Echoes of Bathala* and `UltraGate` to provide one composable library with:

- True open-loop pacing (`OpenLoopPacer`) returning scheduled timestamps to eliminate coordinated omission and track schedule slips.
- Cache-resident, 64-byte-aligned HDR log-linear histograms (`NativeHistogram`, ~30.7 KB) with an 8-byte blittable `NativeHistogramHandle` that passes directly into `delegate*` callbacks without `GCHandle`.
- Clean 3-layer separation between workload authoring (`IMacroProbe`), execution and live progress (`ProbeRunner` + `IProbeLogger`), and presentation/artifacts (`IProbeExporter`: Console, JSON, Markdown, CSV).
- A bundled compile-time Roslyn source generator (`[MacroProbe]`, `[ProbeParams]`, `[ProbeBudget]`) emitting a static, NativeAOT-safe `GeneratedProbes.CreateAll()` registry.

---

## Installation

```bash
dotnet add package ZeroAlloc.MacroBench
```

The Roslyn incremental source generator is bundled inside the same package under `analyzers/dotnet/cs`.

---

## Quick Start

```csharp
using MacroBench.Attributes;
using MacroBench.Execution;
using MacroBench.Generated;
using MacroBench.Pacing;
using Platform.Time;

[MacroProbe("Pipeline.Transit")]
[ProbeBudget(MaxP99Ns = 10_000, MaxAllowedDrops = 0, MaxAllowedGcBytes = 0)]
public sealed class PipelineProbe : IMacroProbe
{
    [ProbeParams(64, 256, 1024)]
    public int PayloadBytes { get; set; }

    public string Name => "Pipeline.Transit";

    public void Measure(ProbeContext context)
    {
        var pacer = OpenLoopPacer.CreatePaced(targetOpsPerSec: 100_000);
        long deadlineNs = NativeTsc.NowNs() + (long)context.MeasureDuration.TotalNanoseconds;

        while (NativeTsc.NowNs() < deadlineNs && !context.CancellationToken.IsCancellationRequested)
        {
            long scheduledNs = pacer.WaitNext();
            // ... dispatch work and measure end-to-end latency ...
            long elapsedNs = NativeTsc.NowNs() - scheduledNs;
            context.RecordLatencyNs(elapsedNs);
            context.AddOperations(1);
            context.AddBytes(PayloadBytes);
        }

        context.SetScheduleSlips(pacer.ScheduleSlips);
    }
}

// Program.cs:
return ProbeSwitcher.Run(args, GeneratedProbes.CreateAll());
```

Run from CLI:

```bash
# List discovered probes and parameter sweeps
dotnet run -c Release -- --list

# Run filtered probes, enforce budgets, write JSON/Markdown/CSV artifacts
dotnet run -c Release -- --filter "*Pipeline*" --artifacts ./BenchmarkArtifacts --budget-strict

# Compare against a previous baseline summary.json
dotnet run -c Release -- --filter "*Pipeline*" --baseline ./BenchmarkArtifacts/prev/summary.json
```

---

## Platform Support Matrix

| Component | Linux (`x64` / `arm64`) | Windows (`x64` / `arm64`) | macOS (`arm64` / `x64`) | Notes |
| :--- | :---: | :---: | :---: | :--- |
| `NativeHistogram` & `UnmanagedLinearHistogram` | Full | Full | Full | 64-byte-aligned unmanaged storage; `NativeHistogramHandle` is 8-byte blittable. |
| `OpenLoopPacer` | Full | Full | Full | Backed by `NativeTsc.NowNs()`; returns scheduled timestamps and counts slips. |
| `SeqlockMetricsReader` | Full | Full | Full | Includes `Thread.MemoryBarrier()` fences for ARM64 weak memory ordering. |
| `PhaseBarrier` | Full | Full | Full | Progressive spin-wait + `ThreadParker` two-phase parking. |
| `PinnedWorkerThread` | Full | Full | Best-effort unpinned | `WorkerPinningMode.BestEffort` (default) skips affinity on macOS; `Strict` throws. |
| `ProcessPriorityGuard` & `HostEnvironmentInfo` | Full (`cpufreq`) | Full (`powercfg`) | Full (`pmset`) | Defaults to `ProcessPriorityClass.High` so native service threads are not starved. |
| `ProbeRunner`, `IProbeLogger`, `IProbeExporter` | Full | Full | Full | Console (`NO_COLOR` aware), JSONL, JSON, Markdown, CSV, and baseline diffs. |

---

## When to Use (and When Not to Use)

| Use `ZeroAlloc.MacroBench` when… | Do not use when… |
| :--- | :--- |
| You are benchmarking stateful, multi-threaded pipelines, daemons, IPC rings, or network dataplanes over seconds of sustained load. | You are microbenchmarking isolated single methods (use BenchmarkDotNet). |
| You need coordinated-omission-free open-loop pacing, tail percentiles, `0 B` GC verification, and CI budget gating. | You are load-testing async HTTP/gRPC web APIs (use k6 or NBomber). |

---

## Documentation

- [Writing Probes](./docs/writing-probes.md) — `IMacroProbe` / `IAsyncMacroProbe` lifecycle, `[ProbeParams]` sweeps, `NativeHistogramHandle` in `delegate*` callbacks, `MergeFrom`, `PinnedWorkerThread`, and `PhaseBarrier`.
- [CLI, Artifacts & CI Integration](./docs/cli-and-artifacts.md) — `ProbeSwitcher` interactive menu, CLI flags, `ProbeRunConfig`, JSON/Markdown/CSV schemas, and CI `--baseline` + `--budget-strict` gating.
- [Macro-Benchmarking Methodology](./docs/methodology.md) — coordinated omission, `Min` floor vs. `P50` queue dwell, SMT core pinning, `High` vs. `RealTime` priority, and `NativeHistogram` vs. `UnmanagedLinearHistogram`.

---

## Artifacts Generated

When `--artifacts <dir>` (or `ProbeRunConfig.WithArtifactsDirectory(...)`) is configured, the built-in exporters write:

- `summary.json` — full `HostEnvironmentInfo`, `ProbeReport[]`, custom metrics, and budget results (consumable by `BaselineComparer`).
- `summary.md` — GitHub-flavored Markdown table ready for docs, PRs, or `$GITHUB_STEP_SUMMARY`.
- `summary.csv` — flat tabular metrics per probe/scenario.
- `percentiles.csv` — latency distribution (`50`, `90`, `95`, `99`, `99.9`, `99.99`, `100`) for external plotting scripts.

---

## Building & Testing

```bash
dotnet build ZeroAlloc.MacroBench.slnx --no-incremental -c Release
dotnet test ZeroAlloc.MacroBench.slnx -c Release --no-build
dotnet test ZeroAlloc.MacroBench.slnx -c Debug
```

---

## License

[MIT](./LICENSE) © 2026 Markism-JA
