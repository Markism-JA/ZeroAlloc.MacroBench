using System.Globalization;

namespace MacroBench.Reporting;

/// <summary>
/// Renders an aligned, color-aware summary table across all probes in a <see cref="ProbeRunSummary"/>.
/// </summary>
public sealed class ConsoleSummaryExporter : IProbeExporter
{
    private readonly TextWriter _writer;
    private readonly bool _useColor;

    public string Name => "Console";

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

    public IReadOnlyList<string> Export(ProbeRunSummary summary, string? artifactsDirectory)
    {
        _writer.WriteLine();
        _writer.WriteLine($"// Host: {summary.Host.ToShortSummary()}");
        _writer.WriteLine(
            $"// RunId: {summary.RunId} | Total Duration: {summary.TotalDuration.TotalMilliseconds:F1} ms"
        );
        _writer.WriteLine();

        const string header =
            "| Probe                      | Ops/sec      | P50 (us)  | P99 (us)  | P99.9 (us) | Drops   | GC (B)   | Status |";
        string divider = new('-', header.Length);

        _writer.WriteLine(divider);
        _writer.WriteLine(header);
        _writer.WriteLine(divider);

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
            if (_useColor)
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = status switch
                {
                    "PASS" => ConsoleColor.Green,
                    "FAIL" => ConsoleColor.Red,
                    _ => ConsoleColor.Gray,
                };
                _writer.Write($"{status,-6}");
                Console.ForegroundColor = prev;
                _writer.WriteLine(" |");
            }
            else
            {
                _writer.WriteLine($"{status,-6} |");
            }
        }

        _writer.WriteLine(divider);
        _writer.WriteLine();
        return Array.Empty<string>();
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..(maxLen - 1)] + "…";
}
