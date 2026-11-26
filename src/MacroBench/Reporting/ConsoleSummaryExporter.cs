using System.Globalization;

namespace MacroBench.Reporting;

/// <summary>
/// Renders an aligned, BDN-style color-coded summary table across all probes in a <see cref="ProbeRunSummary"/>.
/// Automatically disables colors when stdout is redirected or <c>NO_COLOR</c> is set.
/// </summary>
public sealed class ConsoleSummaryExporter : IProbeExporter
{
    private readonly TextWriter _writer;
    private readonly bool _useColor;

    /// <inheritdoc />
    public string Name => "Console";

    /// <summary>Creates a console summary exporter writing to <paramref name="writer"/> (defaults to <see cref="Console.Out"/>).</summary>
    public ConsoleSummaryExporter(TextWriter? writer = null, bool? useColor = null)
    {
        _writer = writer ?? Console.Out;
        _useColor =
            useColor
            ?? (
                writer is null
                && !Console.IsOutputRedirected
                && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NO_COLOR"))
            );
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Export(ProbeRunSummary summary, string? artifactsDirectory)
    {
        _writer.WriteLine();
        WriteColoredLine($"// Host: {summary.Host.ToShortSummary()}", ConsoleColor.DarkCyan);
        WriteColoredLine(
            $"// RunId: {summary.RunId} | Total Duration: {summary.TotalDuration.TotalMilliseconds:F1} ms",
            ConsoleColor.DarkCyan
        );
        _writer.WriteLine();

        const string header =
            "| Probe                      | Ops/sec      | P50 (us)  | P99 (us)  | P99.9 (us) | Drops   | GC (B)   | Status |";
        string divider = new('-', header.Length);

        WriteColoredLine(divider, ConsoleColor.DarkGray);
        WriteColoredLine(header, ConsoleColor.Cyan);
        WriteColoredLine(divider, ConsoleColor.DarkGray);

        foreach (ProbeReport r in summary.Reports)
        {
            string name = Truncate(
                string.IsNullOrEmpty(r.Scenario) ? r.ProbeName : $"{r.ProbeName} ({r.Scenario})",
                26
            );
            string status = r.BudgetResult is null
                ? "OK"
                : (r.BudgetResult.Passed ? "PASS" : "FAIL");

            string linePrefix = string.Format(
                CultureInfo.InvariantCulture,
                "| {0,-26} | {1,12:N0} | {2,9:F2} | {3,9:F2} | {4,10:F2} | {5,7:N0} | {6,8:N0} | ",
                name,
                r.ThroughputOpsSec,
                r.Latency.P50Ns / 1_000.0,
                r.Latency.P99Ns / 1_000.0,
                r.Latency.P999Ns / 1_000.0,
                r.DroppedOperations,
                r.GcAllocatedBytes
            );

            _writer.Write(linePrefix);
            ConsoleColor statusColor = status switch
            {
                "PASS" => ConsoleColor.Green,
                "FAIL" => ConsoleColor.Red,
                _ => ConsoleColor.Gray,
            };
            WriteColored($"{status,-6}", statusColor);
            _writer.WriteLine(" |");
        }

        WriteColoredLine(divider, ConsoleColor.DarkGray);

        bool hasCustom = summary.Reports.Any(r => r.CustomMetrics is { Count: > 0 });
        if (hasCustom)
        {
            WriteColoredLine("Custom Metrics:", ConsoleColor.Cyan);
            foreach (ProbeReport r in summary.Reports)
            {
                if (r.CustomMetrics is not { Count: > 0 })
                    continue;

                string label = string.IsNullOrEmpty(r.Scenario) ? r.ProbeName : $"{r.ProbeName} ({r.Scenario})";
                string pairs = string.Join(
                    ", ",
                    r.CustomMetrics.Select(kvp => string.Format(CultureInfo.InvariantCulture, "{0}={1:G}", kvp.Key, kvp.Value))
                );
                _writer.WriteLine($"  - {label}: {pairs}");
            }
        }

        _writer.WriteLine();
        return Array.Empty<string>();
    }

    private void WriteColored(string text, ConsoleColor color)
    {
        if (!_useColor)
        {
            _writer.Write(text);
            return;
        }

        ConsoleColor prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        _writer.Write(text);
        Console.ForegroundColor = prev;
    }

    private void WriteColoredLine(string text, ConsoleColor color)
    {
        WriteColored(text, color);
        _writer.WriteLine();
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..(maxLen - 1)] + "…";
}
