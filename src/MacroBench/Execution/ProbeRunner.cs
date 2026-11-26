using System.Diagnostics;
using MacroBench.Environment;
using MacroBench.Logging;
using MacroBench.Reporting;
using MacroBench.Telemetry;
using Platform.Time;
using Platform.Timer;

namespace MacroBench.Execution;

/// <summary>
/// Orchestrates <see cref="IMacroProbe"/> lifecycles, GC delta tracking, Seqlock live progress polling,
/// budget evaluation, and decoupled artifact export.
/// </summary>
public static class ProbeRunner
{
    /// <summary>Runs the specified probes using <see cref="ProbeRunConfig.Default"/>.</summary>
    public static ProbeRunSummary Run(params IMacroProbe[] probes) =>
        Run(ProbeRunConfig.Default, probes, CancellationToken.None);

    /// <summary>Runs the specified probes synchronously using <paramref name="config"/> and returns the immutable run summary.</summary>
    public static ProbeRunSummary Run(
        ProbeRunConfig config,
        IEnumerable<IMacroProbe> probes,
        CancellationToken cancellationToken = default
    ) => RunAsync(config, probes, cancellationToken).GetAwaiter().GetResult();

    /// <summary>Asynchronously runs the specified probes (awaiting <see cref="IAsyncMacroProbe"/> natively) using <see cref="ProbeRunConfig.Default"/>.</summary>
    public static Task<ProbeRunSummary> RunAsync(params IMacroProbe[] probes) =>
        RunAsync(ProbeRunConfig.Default, probes, CancellationToken.None);

    /// <summary>Asynchronously runs the specified probes (awaiting <see cref="IAsyncMacroProbe"/> natively) and returns the immutable run summary.</summary>
    public static async Task<ProbeRunSummary> RunAsync(
        ProbeRunConfig config,
        IEnumerable<IMacroProbe> probes,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(probes);

        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        string runId = startedAtUtc.ToString("yyyy-MM-dd_HH-mm-ss");
        long suiteStartNs = NativeTsc.NowNs();

        if (config.ElevateProcessPriority)
        {
            ProcessPriorityGuard.TryElevate(config.PreferredPriority);
        }

        using HighResolutionTimerScope? timerScope = config.UseHighResolutionTimerScope
            ? new HighResolutionTimerScope(1)
            : null;

        HostEnvironmentInfo host = HostEnvironmentInfo.Capture();
        Emit(config, ProbeLogEventKind.Environment, null, null, host.ToShortSummary());

        if (!string.IsNullOrEmpty(host.ThrottlingWarning))
        {
            Emit(config, ProbeLogEventKind.Warning, null, null, host.ThrottlingWarning);
        }

        var reports = new List<ProbeReport>();
        foreach (IMacroProbe probe in probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(await ExecuteSingleProbeAsync(probe, host, config, cancellationToken).ConfigureAwait(false));
        }

        TimeSpan totalDuration = TimeSpan.FromTicks(
            Math.Max(0, (NativeTsc.NowNs() - suiteStartNs) / 100L)
        );
        var summary = new ProbeRunSummary(runId, startedAtUtc, totalDuration, host, reports);

        string? outputDir = null;
        if (!string.IsNullOrWhiteSpace(config.ArtifactsDirectory))
        {
            outputDir = config.CreateTimestampedRunFolder
                ? Path.Combine(config.ArtifactsDirectory, runId)
                : config.ArtifactsDirectory;
        }

        var writtenFiles = new List<string>();
        foreach (IProbeExporter exporter in config.Exporters)
        {
            IReadOnlyList<string> files = exporter.Export(summary, outputDir);
            if (files.Count > 0)
                writtenFiles.AddRange(files);
        }

        if (writtenFiles.Count > 0)
        {
            Emit(
                config,
                ProbeLogEventKind.ArtifactsWritten,
                null,
                null,
                $"Wrote {writtenFiles.Count} artifact(s) to {outputDir}"
            );
        }

        if (config.FailOnBudgetViolation && !summary.AllBudgetsPassed)
        {
            var failures = summary
                .Reports.Where(r => r.BudgetResult is { Passed: false })
                .Select(r => $"{r.ProbeName}: {string.Join("; ", r.BudgetResult!.Violations)}");

            throw new PerformanceBudgetViolationException(
                $"[BUDGET VIOLATION] {string.Join(" | ", failures)}"
            );
        }

        return summary;
    }

    private static IAsyncMacroProbe? UnwrapAsync(IMacroProbe probe) =>
        probe switch
        {
            IAsyncMacroProbe asyncProbe => asyncProbe,
            ConfiguredMacroProbe { Inner: IAsyncMacroProbe innerAsync } => innerAsync,
            _ => null,
        };

    private static async Task<ProbeReport> ExecuteSingleProbeAsync(
        IMacroProbe probe,
        HostEnvironmentInfo host,
        ProbeRunConfig config,
        CancellationToken cancellationToken
    )
    {
        IAsyncMacroProbe? asyncProbe = UnwrapAsync(probe);
        using var context = new ProbeContext(
            host,
            config.WarmupDuration,
            config.MeasureDuration,
            cancellationToken
        );

        try
        {
            Emit(config, ProbeLogEventKind.PhaseStarted, probe.Name, "Setup", "Initializing probe resources");
            if (asyncProbe is not null)
                await asyncProbe.SetupAsync(context).ConfigureAwait(false);
            else
                probe.Setup(context);

            Emit(config, ProbeLogEventKind.PhaseStarted, probe.Name, "Warmup", $"Warming up ({config.WarmupDuration.TotalMilliseconds:F0} ms target)");
            if (asyncProbe is not null)
                await asyncProbe.WarmupAsync(context).ConfigureAwait(false);
            else
                probe.Warmup(context);

            context.ResetForMeasurement();
            if (config.CollectGcBeforeMeasure)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Emit(config, ProbeLogEventKind.PhaseStarted, probe.Name, "Measure", $"Measuring ({config.MeasureDuration.TotalMilliseconds:F0} ms target)");

            long measureStartNs = NativeTsc.NowNs();
            using Timer? progressTimer = StartLiveProgressTimer(probe.Name, context, config, measureStartNs);

            GcDeltaTracker gcTracker = GcDeltaTracker.Start();
            if (asyncProbe is not null)
                await asyncProbe.MeasureAsync(context).ConfigureAwait(false);
            else
                probe.Measure(context);
            GcDelta gcDelta = gcTracker.Stop();

            long measureElapsedNs = Math.Max(1, NativeTsc.NowNs() - measureStartNs);
            progressTimer?.Dispose();

            HistogramSnapshot snapshot = context.Histogram.Snapshot();
            long totalOps = context.Operations > 0 ? context.Operations : snapshot.TotalCount;
            double seconds = measureElapsedNs / 1_000_000_000.0;
            double opsPerSec = seconds > 0 ? totalOps / seconds : 0.0;
            double bandwidthMbSec =
                seconds > 0 && context.BytesTransferred > 0
                    ? (context.BytesTransferred / (1024.0 * 1024.0)) / seconds
                    : 0.0;

            BudgetEvaluationResult? budgetResult = probe.Budget?.EvaluateDetailed(
                in snapshot,
                context.DroppedOperations,
                in gcDelta,
                opsPerSec / 1_000_000.0
            );

            var report = new ProbeReport(
                ProbeName: probe.Name,
                Scenario: probe.Scenario,
                Duration: TimeSpan.FromTicks(measureElapsedNs / 100L),
                TotalOperations: totalOps,
                ThroughputOpsSec: opsPerSec,
                BandwidthMBSec: bandwidthMbSec,
                Latency: snapshot,
                DroppedOperations: context.DroppedOperations,
                Gc: gcDelta,
                TopologySummary: host.ToShortSummary(),
                ScheduleSlips: context.ScheduleSlips,
                BudgetResult: budgetResult,
                CustomMetrics: context.CustomMetrics is { Count: > 0 }
                    ? new Dictionary<string, double>(context.CustomMetrics)
                    : null
            );

            if (budgetResult is { Passed: false })
            {
                Emit(
                    config,
                    ProbeLogEventKind.BudgetViolated,
                    probe.Name,
                    "Budget",
                    string.Join("; ", budgetResult.Violations)
                );
            }
            else
            {
                Emit(
                    config,
                    ProbeLogEventKind.ProbeCompleted,
                    probe.Name,
                    "Complete",
                    $"{opsPerSec:N0} ops/s | p50 {snapshot.P50Ns / 1_000.0:F2} us | p99 {snapshot.P99Ns / 1_000.0:F2} us | drops {context.DroppedOperations:N0} | gc {gcDelta.AllocatedBytes:N0} B",
                    totalOps,
                    opsPerSec,
                    context.DroppedOperations,
                    report.Duration
                );
            }

            return report;
        }
        finally
        {
            Emit(config, ProbeLogEventKind.PhaseStarted, probe.Name, "Teardown", "Releasing probe resources");
            if (asyncProbe is not null)
                await asyncProbe.TeardownAsync(context).ConfigureAwait(false);
            else
                probe.Teardown(context);
        }
    }

    private static unsafe Timer? StartLiveProgressTimer(
        string probeName,
        ProbeContext context,
        ProbeRunConfig config,
        long measureStartNs
    )
    {
        if (config.LiveProgressInterval <= TimeSpan.Zero || config.Loggers.Count == 0)
            return null;

        LiveProgressBlock* progressPtr = context.LiveProgressPointer;
        return new Timer(
            _ =>
            {
                if (!SeqlockMetricsReader.TryRead(progressPtr, out LiveProgressBlock block))
                    return;

                if (block.Operations <= 0)
                    return;

                long elapsedNs = Math.Max(1, NativeTsc.NowNs() - measureStartNs);
                double sec = elapsedNs / 1_000_000_000.0;
                double rate = block.Operations / sec;
                var elapsed = TimeSpan.FromTicks(elapsedNs / 100L);

                Emit(
                    config,
                    ProbeLogEventKind.ProgressTick,
                    probeName,
                    "Measure",
                    $"{elapsed.TotalSeconds:F1}s | {rate:N0} ops/s | ops {block.Operations:N0} | drops {block.DroppedOperations:N0}",
                    block.Operations,
                    rate,
                    block.DroppedOperations,
                    elapsed
                );
            },
            null,
            config.LiveProgressInterval,
            config.LiveProgressInterval
        );
    }

    private static void Emit(
        ProbeRunConfig config,
        ProbeLogEventKind kind,
        string? probeName,
        string? phase,
        string message,
        long? operations = null,
        double? opsPerSec = null,
        long? droppedOperations = null,
        TimeSpan? elapsed = null
    )
    {
        if (config.Loggers.Count == 0)
            return;

        var ev = new ProbeLogEvent(
            DateTimeOffset.UtcNow,
            kind,
            probeName,
            phase,
            message,
            operations,
            opsPerSec,
            droppedOperations,
            elapsed
        );

        foreach (IProbeLogger logger in config.Loggers)
        {
            logger.Log(in ev);
        }
    }
}
