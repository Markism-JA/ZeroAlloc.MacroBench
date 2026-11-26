namespace MacroBench.Telemetry;

public sealed class PerformanceBudgetViolationException(string message) : Exception(message);

/// <summary>
/// Outcome of evaluating a probe run against a <see cref="PerformanceBudget"/>.
/// </summary>
public sealed record BudgetEvaluationResult(bool Passed, IReadOnlyList<string> Violations)
{
    public static BudgetEvaluationResult Pass { get; } = new(true, Array.Empty<string>());
}

/// <summary>
/// Hard performance contract budget for automated regression gating.
/// </summary>
public readonly record struct PerformanceBudget(
    long MaxP50Ns = long.MaxValue,
    long MaxP99Ns = long.MaxValue,
    long MaxP999Ns = long.MaxValue,
    long MaxAllowedDrops = long.MaxValue,
    long MaxAllowedGcBytes = long.MaxValue,
    double MinThroughputMpps = 0.0,
    int MaxGen0Collections = int.MaxValue
)
{
    /// <summary>
    /// Evaluates execution metrics against all configured thresholds and returns every violation.
    /// </summary>
    public BudgetEvaluationResult EvaluateDetailed(
        in HistogramSnapshot latency,
        long drops,
        in GcDelta gc,
        double achievedMpps
    )
    {
        List<string>? violations = null;

        if (gc.AllocatedBytes > MaxAllowedGcBytes)
        {
            (violations ??= []).Add(
                $"GC allocations {gc.AllocatedBytes:N0} B > budget {MaxAllowedGcBytes:N0} B"
            );
        }

        if (gc.Gen0Collections > MaxGen0Collections)
        {
            (violations ??= []).Add(
                $"Gen0 collections {gc.Gen0Collections} > budget {MaxGen0Collections}"
            );
        }

        if (drops > MaxAllowedDrops)
        {
            (violations ??= []).Add(
                $"Dropped ops {drops:N0} > budget {MaxAllowedDrops:N0}"
            );
        }

        if (latency.P50Ns > MaxP50Ns)
        {
            (violations ??= []).Add(
                $"P50 latency {latency.P50Ns:N0} ns > budget {MaxP50Ns:N0} ns"
            );
        }

        if (latency.P99Ns > MaxP99Ns)
        {
            (violations ??= []).Add(
                $"P99 latency {latency.P99Ns:N0} ns > budget {MaxP99Ns:N0} ns"
            );
        }

        if (latency.P999Ns > MaxP999Ns)
        {
            (violations ??= []).Add(
                $"P99.9 latency {latency.P999Ns:N0} ns > budget {MaxP999Ns:N0} ns"
            );
        }

        if (achievedMpps < MinThroughputMpps)
        {
            (violations ??= []).Add(
                $"Throughput {achievedMpps:F3} Mpps < floor {MinThroughputMpps:F3} Mpps"
            );
        }

        return violations is null
            ? BudgetEvaluationResult.Pass
            : new BudgetEvaluationResult(false, violations);
    }

    /// <summary>
    /// Evaluates execution metrics against the budget, throwing a detailed
    /// <see cref="PerformanceBudgetViolationException"/> if any threshold is violated.
    /// </summary>
    public void Evaluate(
        in HistogramSnapshot latency,
        long drops,
        long gcAllocatedBytes,
        double achievedMpps,
        int gen0Collections = 0
    )
    {
        var gc = new GcDelta(gcAllocatedBytes, gen0Collections, 0, 0);
        BudgetEvaluationResult result = EvaluateDetailed(in latency, drops, in gc, achievedMpps);
        if (!result.Passed)
        {
            throw new PerformanceBudgetViolationException(
                $"[BUDGET VIOLATION] {string.Join("; ", result.Violations)}"
            );
        }
    }
}
