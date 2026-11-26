using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroBench.Logging;

/// <summary>
/// Writes structured <see cref="ProbeLogEvent"/> entries as newline-delimited JSON (JSONL).
/// </summary>
public sealed class JsonLinesProbeLogger : IProbeLogger, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly Lock _sync = new();

    /// <summary>Creates a JSONL logger writing to an existing <see cref="TextWriter"/>.</summary>
    public JsonLinesProbeLogger(TextWriter writer, bool ownsWriter = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _ownsWriter = ownsWriter;
    }

    /// <summary>Creates a JSONL logger writing to a file path.</summary>
    public JsonLinesProbeLogger(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(filePath, append: false);
        _ownsWriter = true;
    }

    /// <inheritdoc />
    public void Log(in ProbeLogEvent logEvent)
    {
        string line = JsonSerializer.Serialize(logEvent, SerializerOptions);
        lock (_sync)
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    /// <summary>Disposes the underlying writer if owned by this logger.</summary>
    public void Dispose()
    {
        if (_ownsWriter)
            _writer.Dispose();
    }
}
