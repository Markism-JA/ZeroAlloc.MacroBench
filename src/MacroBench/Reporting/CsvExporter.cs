using System.Globalization;
using System.Text;

namespace MacroBench.Reporting;

/// <summary>
/// Exports machine-readable CSV artifacts (<c>summary.csv</c> and <c>percentiles.csv</c>) for plotting and analysis.
/// </summary>
public sealed class CsvExporter : IProbeExporter
{
    public string Name => "Csv";

    public IReadOnlyList<string> Export(ProbeRunSummary summary, string? artifactsDirectory)
    {
        if (string.IsNullOrWhiteSpace(artifactsDirectory))
            return Array.Empty<string>();

        Directory.CreateDirectory(artifactsDirectory);

        string summaryPath = Path.Combine(artifactsDirectory, "summary.csv");
        string percentilesPath = Path.Combine(artifactsDirectory, "percentiles.csv");

        var summarySb = new StringBuilder();
        summarySb.AppendLine(
            "ProbeName,Scenario,DurationMs,TotalOperations,ThroughputOpsSec,BandwidthMBSec,MinNs,MeanNs,P50Ns,P90Ns,P95Ns,P99Ns,P999Ns,P9999Ns,MaxNs,DroppedOperations,ScheduleSlips,GcAllocatedBytes,Gen0,Gen1,Gen2,BudgetPassed"
        );

        var pctSb = new StringBuilder();
        pctSb.AppendLine("ProbeName,Scenario,Percentile,LatencyNs");

        foreach (ProbeReport r in summary.Reports)
        {
            string passed = r.BudgetResult is null ? "" : r.BudgetResult.Passed.ToString();
            summarySb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2:F3},{3},{4:F3},{5:F3},{6},{7:F3},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21}",
                    Escape(r.ProbeName),
                    Escape(r.Scenario),
                    r.Duration.TotalMilliseconds,
                    r.TotalOperations,
                    r.ThroughputOpsSec,
                    r.BandwidthMBSec,
                    r.Latency.MinNs,
                    r.Latency.MeanNs,
                    r.Latency.P50Ns,
                    r.Latency.P90Ns,
                    r.Latency.P95Ns,
                    r.Latency.P99Ns,
                    r.Latency.P999Ns,
                    r.Latency.P9999Ns,
                    r.Latency.MaxNs,
                    r.DroppedOperations,
                    r.ScheduleSlips,
                    r.Gc.AllocatedBytes,
                    r.Gc.Gen0Collections,
                    r.Gc.Gen1Collections,
                    r.Gc.Gen2Collections,
                    passed
                )
            );

            AppendPercentile(pctSb, r, "50", r.Latency.P50Ns);
            AppendPercentile(pctSb, r, "90", r.Latency.P90Ns);
            AppendPercentile(pctSb, r, "95", r.Latency.P95Ns);
            AppendPercentile(pctSb, r, "99", r.Latency.P99Ns);
            AppendPercentile(pctSb, r, "99.9", r.Latency.P999Ns);
            AppendPercentile(pctSb, r, "99.99", r.Latency.P9999Ns);
            AppendPercentile(pctSb, r, "100", r.Latency.MaxNs);
        }

        File.WriteAllText(summaryPath, summarySb.ToString());
        File.WriteAllText(percentilesPath, pctSb.ToString());
        return [summaryPath, percentilesPath];
    }

    private static void AppendPercentile(StringBuilder sb, ProbeReport r, string pct, long ns)
    {
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3}",
                Escape(r.ProbeName),
                Escape(r.Scenario),
                pct,
                ns
            )
        );
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
