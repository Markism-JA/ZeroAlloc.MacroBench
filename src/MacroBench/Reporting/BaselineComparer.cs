using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MacroBench.Reporting;

/// <summary>
/// Per-probe comparison between a baseline run and a current run.
/// </summary>
public sealed record ProbeBaselineDiff(
    string ProbeName,
    string Scenario,
    double BaselineOpsSec,
    double CurrentOpsSec,
    double ThroughputDeltaPct,
    long BaselineP50Ns,
    long CurrentP50Ns,
    double P50DeltaPct,
    long BaselineP99Ns,
    long CurrentP99Ns,
    double P99DeltaPct,
    long BaselineGcBytes,
    long CurrentGcBytes
);

/// <summary>
/// Compares a <see cref="ProbeRunSummary"/> against a previously exported <c>summary.json</c> baseline.
/// </summary>
public static class BaselineComparer
{
    public static ProbeRunSummary LoadSummary(string jsonFilePath)
    {
        string json = File.ReadAllText(jsonFilePath);
        return JsonSerializer.Deserialize<ProbeRunSummary>(json)
            ?? throw new InvalidDataException($"Failed to deserialize ProbeRunSummary from '{jsonFilePath}'.");
    }

    public static IReadOnlyList<ProbeBaselineDiff> Compare(
        ProbeRunSummary baseline,
        ProbeRunSummary current
    )
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baselineMap = new Dictionary<(string Name, string Scenario), ProbeReport>();
        foreach (ProbeReport r in baseline.Reports)
        {
            baselineMap[(r.ProbeName, r.Scenario)] = r;
        }

        var diffs = new List<ProbeBaselineDiff>();
        foreach (ProbeReport cur in current.Reports)
        {
            if (!baselineMap.TryGetValue((cur.ProbeName, cur.Scenario), out ProbeReport? b))
                continue;

            diffs.Add(
                new ProbeBaselineDiff(
                    ProbeName: cur.ProbeName,
                    Scenario: cur.Scenario,
                    BaselineOpsSec: b.ThroughputOpsSec,
                    CurrentOpsSec: cur.ThroughputOpsSec,
                    ThroughputDeltaPct: PercentDelta(b.ThroughputOpsSec, cur.ThroughputOpsSec),
                    BaselineP50Ns: b.Latency.P50Ns,
                    CurrentP50Ns: cur.Latency.P50Ns,
                    P50DeltaPct: PercentDelta(b.Latency.P50Ns, cur.Latency.P50Ns),
                    BaselineP99Ns: b.Latency.P99Ns,
                    CurrentP99Ns: cur.Latency.P99Ns,
                    P99DeltaPct: PercentDelta(b.Latency.P99Ns, cur.Latency.P99Ns),
                    BaselineGcBytes: b.GcAllocatedBytes,
                    CurrentGcBytes: cur.GcAllocatedBytes
                )
            );
        }

        return diffs;
    }

    public static string RenderMarkdown(IReadOnlyList<ProbeBaselineDiff> diffs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Probe | Scenario | Ops/sec Delta | P50 Delta | P99 Delta | GC (B) |");
        sb.AppendLine("| :--- | :--- | ---: | ---: | ---: | ---: |");

        foreach (ProbeBaselineDiff d in diffs)
        {
            sb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "| `{0}` | {1} | {2:+0.0;-0.0;0.0}% | {3:+0.0;-0.0;0.0}% | {4:+0.0;-0.0;0.0}% | {5:N0} -> {6:N0} |",
                    d.ProbeName,
                    string.IsNullOrEmpty(d.Scenario) ? "—" : d.Scenario,
                    d.ThroughputDeltaPct,
                    d.P50DeltaPct,
                    d.P99DeltaPct,
                    d.BaselineGcBytes,
                    d.CurrentGcBytes
                )
            );
        }

        return sb.ToString();
    }

    private static double PercentDelta(double baseline, double current)
    {
        if (Math.Abs(baseline) < 1e-9)
            return Math.Abs(current) < 1e-9 ? 0.0 : 100.0;

        return ((current - baseline) / baseline) * 100.0;
    }
}
