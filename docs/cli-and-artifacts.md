# CLI, Artifacts & CI Integration

## 1. CLI Entry Point (`ProbeSwitcher`)

Wire `Program.cs` to `ProbeSwitcher.RunAsync` using the compile-time generated registry:

```csharp
using MacroBench.Execution;
using MacroBench.Generated;

return await ProbeSwitcher.RunAsync(args, GeneratedProbes.CreateAll());
```

### Interactive Selection
When you run without `--filter` or `--all` in an interactive terminal (and more than one probe is registered), `ProbeSwitcher` displays a numbered menu:

```text
Available MacroProbes:
  [1] Ring.Transit (PayloadBytes=64, TargetOpsPerSec=100000)
  [2] Ring.Transit (PayloadBytes=256, TargetOpsPerSec=100000)
  [3] Broker.RoundTrip
Select probe(s) by number (e.g. '1' or '1,3'), glob (e.g. '*Ring*'), or '*' for all:
```

In CI (when stdin is redirected) or when `--filter` / `--all` is passed, the prompt is skipped automatically so builds never hang.

### Supported CLI Flags

| Flag | Description |
| :--- | :--- |
| `--list` | Prints all discovered probes (filtered if `--filter` is also passed) and exits `0`. |
| `--filter <glob>` | Case-insensitive wildcard filter (`*`, `?`) matching either `Name` or `Scenario`. |
| `--all` | Runs all discovered probes without prompting. |
| `--artifacts <dir>` | Directory where exporters write `summary.json`, `summary.md`, `summary.csv`, `percentiles.csv`, and (when present) `custom_metrics.csv`. |
| `--warmup-ms <ms>` | Overrides `ProbeContext.WarmupDuration`. |
| `--duration-ms <ms>` | Overrides `ProbeContext.MeasureDuration`. |
| `--budget-strict` | Enforces `[ProbeBudget]` contracts and returns exit code `1` on any violation. |
| `--baseline <path>` | Loads a previous `summary.json`, prints `%` deltas for throughput/P50/P99/GC via `BaselineComparer`. |
| `--no-color` | Disables ANSI/console colors (also disabled automatically when stdout is redirected or `NO_COLOR` is set). |

---

## 2. Programmatic Execution (`ProbeRunner` + `ProbeRunConfig`)

You can run probes directly inside xUnit integration tests or custom harnesses without `ProbeSwitcher`:

```csharp
using MacroBench.Execution;
using MacroBench.Logging;
using MacroBench.Reporting;

var config = ProbeRunConfig.Default
    .WithArtifactsDirectory("./BenchmarkArtifacts")
    .WithDurations(warmup: TimeSpan.FromMilliseconds(500), measure: TimeSpan.FromSeconds(3))
    .WithLoggers(
        new ConsoleProbeLogger(),
        new JsonLinesProbeLogger("./BenchmarkArtifacts/events.jsonl")
    )
    .WithExporters(Exporters.Console, Exporters.Json, Exporters.Markdown, Exporters.Csv)
    .WithBudgetEnforcement(failOnViolation: true);

ProbeRunSummary summary = await ProbeRunner.RunAsync(config, [new RingTransitProbe()]);
```

---

## 3. Generated Artifact Schemas

When `ArtifactsDirectory` is set (defaulting to a timestamped subfolder `<dir>/<yyyy-MM-dd_HH-mm-ss>/`), the built-in exporters produce:

- **`summary.json`:** Full serialized `ProbeRunSummary` (`RunId`, `StartedAtUtc`, `TotalDuration`, `Host`, `Reports[]` including `Latency`, `Gc`, `ScheduleSlips`, `BudgetResult`, and `CustomMetrics`). This file is the input to `--baseline` and `BaselineComparer.LoadSummary(...)`.
- **`summary.md`:** GitHub-flavored Markdown summary table (plus a `## Custom Metrics` table when any probe records custom metrics). Ready to paste into docs or append to `$GITHUB_STEP_SUMMARY`.
- **`summary.csv`:** One flat row per probe/scenario:
  `ProbeName,Scenario,DurationMs,TotalOperations,ThroughputOpsSec,BandwidthMBSec,MinNs,MeanNs,P50Ns,P90Ns,P95Ns,P99Ns,P999Ns,P9999Ns,MaxNs,DroppedOperations,ScheduleSlips,GcAllocatedBytes,Gen0,Gen1,Gen2,BudgetPassed`
- **`percentiles.csv`:** Long-format distribution (`50`, `90`, `95`, `99`, `99.9`, `99.99`, `100`) for plotting latency curves:
  `ProbeName,Scenario,Percentile,LatencyNs`
- **`custom_metrics.csv`** *(only written if custom metrics exist)*:
  `ProbeName,Scenario,MetricName,Value`

---

## 4. Recommended Git Hygiene & CI Workflow

- Add `BenchmarkArtifacts/` to `.gitignore` so local runs never clutter git history.
- Keep one curated `docs/benchmarks/baseline.json` (copied from a clean reference run's `summary.json`) checked into the repo for regression comparisons.
- Synthesize `summary.md` and external chart plots (from `summary.csv` / `percentiles.csv`) into `docs/benchmarks/README.md` on release milestones.

Example GitHub Actions step:

```yaml
- name: Run MacroBench Regression Gate
  run: |
    dotnet run -c Release --project benchmark/MySystem.MacroHarness -- \
      --all \
      --artifacts ./BenchmarkArtifacts \
      --baseline ./docs/benchmarks/baseline.json \
      --budget-strict
    cat ./BenchmarkArtifacts/*/summary.md >> "$GITHUB_STEP_SUMMARY"
```
