using System.Globalization;
using System.Text;

namespace MacroBench.Reporting;

/// <summary>
/// Exports a GitHub-flavored Markdown report (<c>summary.md</c>) suitable for docs or CI step summaries.
/// </summary>
public sealed class MarkdownExporter : IProbeExporter
{
    /// <inheritdoc />
    public string Name => "Markdown";

    /// <inheritdoc />
    public IReadOnlyList<string> Export(ProbeRunSummary summary, string? artifactsDirectory)
    {
        if (string.IsNullOrWhiteSpace(artifactsDirectory))
            return Array.Empty<string>();

        Directory.CreateDirectory(artifactsDirectory);
        string path = Path.Combine(artifactsDirectory, "summary.md");
        File.WriteAllText(path, Render(summary));
        return [path];
    }

    /// <summary>Renders the run summary as a GitHub-flavored Markdown string.</summary>
    public static string Render(ProbeRunSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# MacroBench Summary (`{summary.RunId}`)");
        sb.AppendLine();
        sb.AppendLine($"- Host: `{summary.Host.ToShortSummary()}`");
        sb.AppendLine($"- Started (UTC): `{summary.StartedAtUtc:O}`");
        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "- Total Duration: `{0:F2} ms`",
                summary.TotalDuration.TotalMilliseconds
            )
        );
        if (!string.IsNullOrEmpty(summary.Host.ThrottlingWarning))
        {
            sb.AppendLine($"- Warning: {summary.Host.ThrottlingWarning}");
        }

        sb.AppendLine();
        sb.AppendLine(
            "| Probe | Scenario | Ops | Ops/sec | Min (µs) | P50 (µs) | P90 (µs) | P99 (µs) | P99.9 (µs) | Max (µs) | Drops | GC (B) | Budget |"
        );
        sb.AppendLine(
            "| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | :---: |"
        );

        foreach (ProbeReport r in summary.Reports)
        {
            string status = r.BudgetResult is null
                ? "—"
                : (r.BudgetResult.Passed ? "PASS" : "FAIL");

            sb.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "| `{0}` | {1} | {2:N0} | {3:N0} | {4:F2} | {5:F2} | {6:F2} | {7:F2} | {8:F2} | {9:F2} | {10:N0} | {11:N0} | {12} |",
                    r.ProbeName,
                    string.IsNullOrEmpty(r.Scenario) ? "—" : r.Scenario,
                    r.TotalOperations,
                    r.ThroughputOpsSec,
                    r.Latency.MinNs / 1_000.0,
                    r.Latency.P50Ns / 1_000.0,
                    r.Latency.P90Ns / 1_000.0,
                    r.Latency.P99Ns / 1_000.0,
                    r.Latency.P999Ns / 1_000.0,
                    r.Latency.MaxNs / 1_000.0,
                    r.DroppedOperations,
                    r.GcAllocatedBytes,
                    status
                )
            );
        }

        if (summary.Reports.Any(r => r.CustomMetrics is { Count: > 0 }))
        {
            sb.AppendLine();
            sb.AppendLine("## Custom Metrics");
            sb.AppendLine();
            sb.AppendLine("| Probe | Scenario | Metric | Value |");
            sb.AppendLine("| :--- | :--- | :--- | ---: |");
            foreach (ProbeReport r in summary.Reports)
            {
                if (r.CustomMetrics is not { Count: > 0 })
                    continue;

                foreach (KeyValuePair<string, double> kvp in r.CustomMetrics)
                {
                    sb.AppendLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "| `{0}` | {1} | `{2}` | {3:G} |",
                            r.ProbeName,
                            string.IsNullOrEmpty(r.Scenario) ? "—" : r.Scenario,
                            kvp.Key,
                            kvp.Value
                        )
                    );
                }
            }
        }

        return sb.ToString();
    }
}
