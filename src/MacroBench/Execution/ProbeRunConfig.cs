using System.Diagnostics;
using MacroBench.Logging;
using MacroBench.Reporting;

namespace MacroBench.Execution;

/// <summary>
/// Immutable configuration controlling <see cref="ProbeRunner"/> execution, logging, and artifact generation.
/// </summary>
public sealed record ProbeRunConfig
{
    /// <summary>Root directory for exported JSON/Markdown/CSV artifacts, or null to disable file exports.</summary>
    public string? ArtifactsDirectory { get; init; }

    /// <summary>When true, creates a timestamped subfolder inside <see cref="ArtifactsDirectory"/> per run.</summary>
    public bool CreateTimestampedRunFolder { get; init; } = true;

    /// <summary>Warmup duration hint exposed on <see cref="ProbeContext.WarmupDuration"/>.</summary>
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Measurement duration hint exposed on <see cref="ProbeContext.MeasureDuration"/>.</summary>
    public TimeSpan MeasureDuration { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Interval for background Seqlock live progress polling; set to <see cref="TimeSpan.Zero"/> to disable.</summary>
    public TimeSpan LiveProgressInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>When true, attempts best-effort process priority elevation before running probes.</summary>
    public bool ElevateProcessPriority { get; init; } = true;

    /// <summary>Preferred process priority class when <see cref="ElevateProcessPriority"/> is enabled.</summary>
    public ProcessPriorityClass PreferredPriority { get; init; } = ProcessPriorityClass.High;

    /// <summary>When true, activates <c>HighResolutionTimerScope</c> (1 ms on Windows; no-op on Linux/macOS).</summary>
    public bool UseHighResolutionTimerScope { get; init; } = true;

    /// <summary>When true, runs a full GC settle between Warmup and Measure phases.</summary>
    public bool CollectGcBeforeMeasure { get; init; } = true;

    /// <summary>When true, throws <see cref="Telemetry.PerformanceBudgetViolationException"/> if any probe fails its budget.</summary>
    public bool FailOnBudgetViolation { get; init; } = false;

    /// <summary>Live lifecycle loggers notified during execution.</summary>
    public IReadOnlyList<IProbeLogger> Loggers { get; init; } = [new ConsoleProbeLogger()];

    /// <summary>Exporters invoked once all probes finish.</summary>
    public IReadOnlyList<IProbeExporter> Exporters { get; init; } =
    [
        Reporting.Exporters.Console,
        Reporting.Exporters.Json,
        Reporting.Exporters.Markdown,
        Reporting.Exporters.Csv,
    ];

    /// <summary>Default run configuration with console logging and Console/JSON/Markdown/CSV exporters.</summary>
    public static ProbeRunConfig Default { get; } = new();

    /// <summary>Returns a copy targeting the specified artifacts directory.</summary>
    public ProbeRunConfig WithArtifactsDirectory(string? directory, bool createTimestampedRunFolder = true) =>
        this with
        {
            ArtifactsDirectory = directory,
            CreateTimestampedRunFolder = createTimestampedRunFolder,
        };

    /// <summary>Returns a copy with updated warmup and measurement duration hints.</summary>
    public ProbeRunConfig WithDurations(TimeSpan warmup, TimeSpan measure) =>
        this with
        {
            WarmupDuration = warmup,
            MeasureDuration = measure,
        };

    /// <summary>Returns a copy with the specified lifecycle loggers.</summary>
    public ProbeRunConfig WithLoggers(params IProbeLogger[] loggers) =>
        this with { Loggers = loggers };

    /// <summary>Returns a copy with the specified result exporters.</summary>
    public ProbeRunConfig WithExporters(params IProbeExporter[] exporters) =>
        this with { Exporters = exporters };

    /// <summary>Returns a copy with budget violation exception enforcement configured.</summary>
    public ProbeRunConfig WithBudgetEnforcement(bool failOnViolation = true) =>
        this with { FailOnBudgetViolation = failOnViolation };
}
