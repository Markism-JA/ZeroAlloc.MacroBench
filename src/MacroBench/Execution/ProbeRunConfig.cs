using System.Diagnostics;
using MacroBench.Logging;
using MacroBench.Reporting;

namespace MacroBench.Execution;

/// <summary>
/// Immutable configuration controlling <see cref="ProbeRunner"/> execution, logging, and artifact generation.
/// </summary>
public sealed record ProbeRunConfig
{
    public string? ArtifactsDirectory { get; init; }
    public bool CreateTimestampedRunFolder { get; init; } = true;
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan MeasureDuration { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan LiveProgressInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public bool ElevateProcessPriority { get; init; } = true;
    public ProcessPriorityClass PreferredPriority { get; init; } = ProcessPriorityClass.High;
    public bool UseHighResolutionTimerScope { get; init; } = true;
    public bool CollectGcBeforeMeasure { get; init; } = true;
    public bool FailOnBudgetViolation { get; init; } = false;
    public IReadOnlyList<IProbeLogger> Loggers { get; init; } = [new ConsoleProbeLogger()];
    public IReadOnlyList<IProbeExporter> Exporters { get; init; } =
    [
        Reporting.Exporters.Console,
        Reporting.Exporters.Json,
        Reporting.Exporters.Markdown,
        Reporting.Exporters.Csv,
    ];

    public static ProbeRunConfig Default { get; } = new();

    public ProbeRunConfig WithArtifactsDirectory(string? directory, bool createTimestampedRunFolder = true) =>
        this with
        {
            ArtifactsDirectory = directory,
            CreateTimestampedRunFolder = createTimestampedRunFolder,
        };

    public ProbeRunConfig WithDurations(TimeSpan warmup, TimeSpan measure) =>
        this with
        {
            WarmupDuration = warmup,
            MeasureDuration = measure,
        };

    public ProbeRunConfig WithLoggers(params IProbeLogger[] loggers) =>
        this with { Loggers = loggers };

    public ProbeRunConfig WithExporters(params IProbeExporter[] exporters) =>
        this with { Exporters = exporters };

    public ProbeRunConfig WithBudgetEnforcement(bool failOnViolation = true) =>
        this with { FailOnBudgetViolation = failOnViolation };
}
