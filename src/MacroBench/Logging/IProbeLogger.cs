namespace MacroBench.Logging;

/// <summary>Classification of lifecycle events emitted by <see cref="Execution.ProbeRunner"/>.</summary>
public enum ProbeLogEventKind : byte
{
    /// <summary>Host hardware/runtime environment snapshot.</summary>
    Environment = 0,

    /// <summary>Non-fatal warning (e.g., CPU throttling or power plan).</summary>
    Warning = 1,

    /// <summary>Probe lifecycle phase transition (Setup, Warmup, Measure, Teardown).</summary>
    PhaseStarted = 2,

    /// <summary>Periodic live progress update polled from Seqlock metrics.</summary>
    ProgressTick = 3,

    /// <summary>Probe completed within budget.</summary>
    ProbeCompleted = 4,

    /// <summary>Probe violated one or more <see cref="Telemetry.PerformanceBudget"/> thresholds.</summary>
    BudgetViolated = 5,

    /// <summary>Exporters finished writing artifact files.</summary>
    ArtifactsWritten = 6,
}

/// <summary>
/// Structured lifecycle event emitted by <see cref="Execution.ProbeRunner"/>.
/// </summary>
public sealed record ProbeLogEvent(
    DateTimeOffset TimestampUtc,
    ProbeLogEventKind Kind,
    string? ProbeName,
    string? Phase,
    string Message,
    long? Operations = null,
    double? OpsPerSec = null,
    long? DroppedOperations = null,
    TimeSpan? Elapsed = null
);

/// <summary>
/// Sink for live macro-benchmark lifecycle and progress events.
/// </summary>
public interface IProbeLogger
{
    /// <summary>Records a structured lifecycle event.</summary>
    void Log(in ProbeLogEvent logEvent);
}
