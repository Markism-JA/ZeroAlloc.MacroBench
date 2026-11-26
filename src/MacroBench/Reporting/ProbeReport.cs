using MacroBench.Telemetry;

namespace MacroBench.Reporting;

/// <summary>
/// Immutable, machine-verifiable report summarizing a single macro-benchmark probe run.
/// </summary>
public sealed record ProbeReport(
    string ProbeName,
    string Scenario,
    TimeSpan Duration,
    long TotalOperations,
    double ThroughputOpsSec,
    double BandwidthMBSec,
    HistogramSnapshot Latency,
    long DroppedOperations,
    GcDelta Gc,
    string TopologySummary,
    long ScheduleSlips = 0,
    BudgetEvaluationResult? BudgetResult = null,
    IReadOnlyDictionary<string, double>? CustomMetrics = null
)
{
    public double AchievedMpps => ThroughputOpsSec / 1_000_000.0;
    public long GcAllocatedBytes => Gc.AllocatedBytes;

    /// <summary>
    /// Evaluates the probe run against a hard performance budget, throwing
    /// <see cref="PerformanceBudgetViolationException"/> if any threshold fails.
    /// </summary>
    public void AssertBudget(in PerformanceBudget budget)
    {
        budget.Evaluate(
            latency: Latency,
            drops: DroppedOperations,
            gcAllocatedBytes: Gc.AllocatedBytes,
            achievedMpps: AchievedMpps,
            gen0Collections: Gc.Gen0Collections
        );
    }
}
