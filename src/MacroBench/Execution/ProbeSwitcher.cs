using System.Text.RegularExpressions;
using MacroBench.Logging;
using MacroBench.Reporting;
using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Lightweight CLI runner for executing, filtering, and baseline-comparing registered <see cref="IMacroProbe"/> instances.
/// Supports <c>--filter</c>, <c>--artifacts</c>, <c>--warmup-ms</c>, <c>--duration-ms</c>,
/// <c>--budget-strict</c>, <c>--baseline</c>, <c>--list</c>, and <c>--no-color</c>.
/// </summary>
public static class ProbeSwitcher
{
    /// <summary>Parses CLI arguments, filters probes, runs them synchronously, and returns 0 on success or 1 on failure.</summary>
    public static int Run(
        string[] args,
        IReadOnlyList<IMacroProbe> probes,
        ProbeRunConfig? baseConfig = null,
        TextWriter? output = null
    ) => RunAsync(args, probes, baseConfig, output).GetAwaiter().GetResult();

    /// <summary>Parses CLI arguments, filters probes, runs them asynchronously, and returns 0 on success or 1 on failure.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        IReadOnlyList<IMacroProbe> probes,
        ProbeRunConfig? baseConfig = null,
        TextWriter? output = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(probes);

        TextWriter writer = output ?? Console.Out;
        string? filter = GetArgValue(args, "--filter");
        string? artifacts = GetArgValue(args, "--artifacts");
        string? baselinePath = GetArgValue(args, "--baseline");
        string? warmupMsStr = GetArgValue(args, "--warmup-ms");
        string? durationMsStr = GetArgValue(args, "--duration-ms");
        bool listOnly = HasFlag(args, "--list");
        bool strictBudget = HasFlag(args, "--budget-strict");
        bool noColor = HasFlag(args, "--no-color");

        IReadOnlyList<IMacroProbe> selected = string.IsNullOrWhiteSpace(filter)
            ? probes
            : probes.Where(p => MatchesGlob(p.Name, filter) || MatchesGlob(p.Scenario, filter)).ToList();

        if (listOnly)
        {
            writer.WriteLine($"Discovered {selected.Count} probe(s):");
            foreach (IMacroProbe p in selected)
            {
                string label = string.IsNullOrEmpty(p.Scenario) ? p.Name : $"{p.Name} ({p.Scenario})";
                writer.WriteLine($"  - {label}");
            }
            return 0;
        }

        if (selected.Count == 0)
        {
            writer.WriteLine($"No probes matched filter '{filter}'.");
            return 1;
        }

        ProbeRunConfig config = baseConfig ?? ProbeRunConfig.Default;
        if (artifacts is not null)
            config = config.WithArtifactsDirectory(artifacts);
        if (int.TryParse(warmupMsStr, out int warmupMs) && warmupMs >= 0)
            config = config with { WarmupDuration = TimeSpan.FromMilliseconds(warmupMs) };
        if (int.TryParse(durationMsStr, out int durationMs) && durationMs > 0)
            config = config with { MeasureDuration = TimeSpan.FromMilliseconds(durationMs) };
        if (strictBudget)
            config = config.WithBudgetEnforcement(true);
        if (noColor)
        {
            config = config
                .WithLoggers(new ConsoleProbeLogger(writer, useColor: false))
                .WithExporters(
                    new ConsoleSummaryExporter(writer, useColor: false),
                    Exporters.Json,
                    Exporters.Markdown,
                    Exporters.Csv
                );
        }

        try
        {
            ProbeRunSummary summary = await ProbeRunner.RunAsync(config, selected, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(baselinePath))
            {
                ProbeRunSummary baseline = BaselineComparer.LoadSummary(baselinePath);
                IReadOnlyList<ProbeBaselineDiff> diffs = BaselineComparer.Compare(baseline, summary);
                if (diffs.Count > 0)
                {
                    writer.WriteLine();
                    writer.WriteLine("Baseline Comparison:");
                    writer.WriteLine(BaselineComparer.RenderMarkdown(diffs));
                }
            }

            return summary.AllBudgetsPassed ? 0 : 1;
        }
        catch (PerformanceBudgetViolationException ex)
        {
            writer.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool MatchesGlob(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }
}
