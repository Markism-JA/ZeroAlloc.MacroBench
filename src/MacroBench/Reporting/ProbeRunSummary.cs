using MacroBench.Environment;

namespace MacroBench.Reporting;

/// <summary>
/// Immutable aggregate summary of a complete <see cref="Execution.ProbeRunner"/> execution.
/// </summary>
public sealed record ProbeRunSummary(
    string RunId,
    DateTimeOffset StartedAtUtc,
    TimeSpan TotalDuration,
    HostEnvironmentInfo Host,
    IReadOnlyList<ProbeReport> Reports
)
{
    public bool AllBudgetsPassed => Reports.All(r => r.BudgetResult is null || r.BudgetResult.Passed);
}
