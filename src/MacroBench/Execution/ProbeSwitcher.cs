using System.Text.RegularExpressions;
using MacroBench.Logging;
using MacroBench.Reporting;
using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Lightweight CLI runner for executing, filtering, interactively selecting, and baseline-comparing
/// registered <see cref="IMacroProbe"/> instances.
/// Supports <c>--filter</c>, <c>--all</c>, <c>--artifacts</c>, <c>--warmup-ms</c>, <c>--duration-ms</c>,
/// <c>--budget-strict</c>, <c>--baseline</c>, <c>--list</c>, and <c>--no-color</c>.
/// </summary>
public static class ProbeSwitcher
{
    /// <summary>Parses CLI arguments, filters or prompts for probes, runs them synchronously, and returns 0 on success or 1 on failure.</summary>
    public static int Run(
        string[] args,
        IReadOnlyList<IMacroProbe> probes,
        ProbeRunConfig? baseConfig = null,
        TextWriter? output = null,
        TextReader? input = null
    ) => RunAsync(args, probes, baseConfig, output, input).GetAwaiter().GetResult();

    /// <summary>Parses CLI arguments, filters or prompts for probes, runs them asynchronously, and returns 0 on success or 1 on failure.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        IReadOnlyList<IMacroProbe> probes,
        ProbeRunConfig? baseConfig = null,
        TextWriter? output = null,
        TextReader? input = null,
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
        bool runAll = HasFlag(args, "--all");
        bool strictBudget = HasFlag(args, "--budget-strict");
        bool noColor = HasFlag(args, "--no-color");
        bool useColor =
            !noColor
            && output is null
            && !Console.IsOutputRedirected
            && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NO_COLOR"));

        IReadOnlyList<IMacroProbe> selected;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            selected = FilterProbes(probes, filter);
        }
        else if (listOnly || runAll || probes.Count <= 1 || (input is null && Console.IsInputRedirected))
        {
            selected = probes;
        }
        else
        {
            selected = PromptInteractiveSelection(probes, writer, input ?? Console.In, useColor);
            if (selected.Count == 0)
                return 1;
        }

        if (listOnly)
        {
            writer.WriteLine($"Discovered {selected.Count} probe(s):");
            for (int i = 0; i < selected.Count; i++)
            {
                writer.WriteLine($"  [{i + 1}] {FormatProbeLabel(selected[i])}");
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

    private static IReadOnlyList<IMacroProbe> PromptInteractiveSelection(
        IReadOnlyList<IMacroProbe> probes,
        TextWriter writer,
        TextReader reader,
        bool useColor
    )
    {
        WriteColoredLine(writer, "Available MacroProbes:", ConsoleColor.Cyan, useColor);
        for (int i = 0; i < probes.Count; i++)
        {
            writer.WriteLine($"  [{i + 1}] {FormatProbeLabel(probes[i])}");
        }

        WriteColored(
            writer,
            "Select probe(s) by number (e.g. '1' or '1,3'), glob (e.g. '*Ring*'), or '*' for all: ",
            ConsoleColor.Yellow,
            useColor
        );

        string? line = reader.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(line))
        {
            writer.WriteLine("No selection entered. Aborting.");
            return Array.Empty<IMacroProbe>();
        }

        if (line == "*")
            return probes;

        string[] tokens = line.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        var chosen = new List<IMacroProbe>();
        var seen = new HashSet<int>();
        bool allNumeric = true;

        foreach (string token in tokens)
        {
            if (int.TryParse(token, out int oneBased) && oneBased >= 1 && oneBased <= probes.Count)
            {
                if (seen.Add(oneBased - 1))
                    chosen.Add(probes[oneBased - 1]);
            }
            else
            {
                allNumeric = false;
                break;
            }
        }

        if (allNumeric && chosen.Count > 0)
            return chosen;

        // Fall back to glob matching against probe name or scenario
        IReadOnlyList<IMacroProbe> matched = FilterProbes(probes, line);
        if (matched.Count == 0)
        {
            writer.WriteLine($"No probes matched selection '{line}'.");
        }
        return matched;
    }

    private static IReadOnlyList<IMacroProbe> FilterProbes(IReadOnlyList<IMacroProbe> probes, string pattern) =>
        probes.Where(p => MatchesGlob(p.Name, pattern) || MatchesGlob(p.Scenario, pattern)).ToList();

    private static string FormatProbeLabel(IMacroProbe probe) =>
        string.IsNullOrEmpty(probe.Scenario) ? probe.Name : $"{probe.Name} ({probe.Scenario})";

    private static void WriteColored(TextWriter writer, string text, ConsoleColor color, bool useColor)
    {
        if (!useColor)
        {
            writer.Write(text);
            return;
        }

        ConsoleColor prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        writer.Write(text);
        Console.ForegroundColor = prev;
    }

    private static void WriteColoredLine(TextWriter writer, string text, ConsoleColor color, bool useColor)
    {
        WriteColored(writer, text, color, useColor);
        writer.WriteLine();
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
