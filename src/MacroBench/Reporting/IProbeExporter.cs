namespace MacroBench.Reporting;

/// <summary>
/// Decoupled presenter/exporter consuming an immutable <see cref="ProbeRunSummary"/>.
/// </summary>
public interface IProbeExporter
{
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
    public static IProbeExporter Console { get; } = new ConsoleSummaryExporter();
    public static IProbeExporter Json { get; } = new JsonExporter();
    public static IProbeExporter Markdown { get; } = new MarkdownExporter();
    public static IProbeExporter Csv { get; } = new CsvExporter();
}
