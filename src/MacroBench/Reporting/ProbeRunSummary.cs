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
    /// <summary>True when every probe with a configured budget passed all checks.</summary>
    public bool AllBudgetsPassed => Reports.All(r => r.BudgetResult is null || r.BudgetResult.Passed);
}
