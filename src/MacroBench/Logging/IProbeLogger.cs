namespace MacroBench.Logging;

public enum ProbeLogEventKind : byte
{
    Environment = 0,
    Warning = 1,
    PhaseStarted = 2,
    ProgressTick = 3,
    ProbeCompleted = 4,
    BudgetViolated = 5,
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
    void Log(in ProbeLogEvent logEvent);
}
