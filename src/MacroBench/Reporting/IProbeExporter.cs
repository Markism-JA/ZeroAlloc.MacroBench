namespace MacroBench.Reporting;

/// <summary>
/// Decoupled presenter/exporter consuming an immutable <see cref="ProbeRunSummary"/>.
/// </summary>
public interface IProbeExporter
{
    /// <summary>Display name of the exporter.</summary>
    string Name { get; }

    /// <summary>
    /// Exports the run summary, optionally writing files into <paramref name="artifactsDirectory"/>
    /// and returning any generated artifact file paths.
    /// </summary>
    IReadOnlyList<string> Export(ProbeRunSummary summary, string? artifactsDirectory);
}

/// <summary>
/// Built-in exporters for console summaries and JSON/Markdown/CSV artifacts.
/// </summary>
public static class Exporters
{
    /// <summary>Default console table exporter.</summary>
    public static IProbeExporter Console { get; } = new ConsoleSummaryExporter();

    /// <summary>Default JSON exporter (<c>summary.json</c>).</summary>
    public static IProbeExporter Json { get; } = new JsonExporter();

    /// <summary>Default Markdown exporter (<c>summary.md</c>).</summary>
    public static IProbeExporter Markdown { get; } = new MarkdownExporter();

    /// <summary>Default CSV exporter (<c>summary.csv</c> and <c>percentiles.csv</c>).</summary>
    public static IProbeExporter Csv { get; } = new CsvExporter();
}
