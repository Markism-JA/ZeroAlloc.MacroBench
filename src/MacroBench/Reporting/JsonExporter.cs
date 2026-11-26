using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroBench.Reporting;

/// <summary>
/// Exports the full <see cref="ProbeRunSummary"/> as formatted JSON (<c>summary.json</c>).
/// </summary>
public sealed class JsonExporter : IProbeExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name => "Json";

    public IReadOnlyList<string> Export(ProbeRunSummary summary, string? artifactsDirectory)
    {
        if (string.IsNullOrWhiteSpace(artifactsDirectory))
            return Array.Empty<string>();

        Directory.CreateDirectory(artifactsDirectory);
        string path = Path.Combine(artifactsDirectory, "summary.json");
        string json = JsonSerializer.Serialize(summary, Options);
        File.WriteAllText(path, json);
        return [path];
    }
}
