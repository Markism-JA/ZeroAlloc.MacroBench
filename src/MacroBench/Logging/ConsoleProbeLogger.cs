namespace MacroBench.Logging;

/// <summary>
/// Color-coded console logger for live probe phases, progress ticks, and budget outcomes.
/// Automatically disables ANSI/console coloring when stdout is redirected or <c>NO_COLOR</c> is set.
/// </summary>
public sealed class ConsoleProbeLogger : IProbeLogger
{
    private readonly TextWriter _writer;
    private readonly bool _useColor;
    private readonly Lock _sync = new();

    /// <summary>Creates a console logger writing to <paramref name="writer"/> (defaults to <see cref="Console.Out"/>).</summary>
    public ConsoleProbeLogger(TextWriter? writer = null, bool? useColor = null)
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
    public void Log(in ProbeLogEvent logEvent)
    {
        (string tag, ConsoleColor color) = logEvent.Kind switch
        {
            ProbeLogEventKind.Environment => ("ENV", ConsoleColor.Cyan),
            ProbeLogEventKind.Warning => ("WARN", ConsoleColor.Yellow),
            ProbeLogEventKind.PhaseStarted => (
                (logEvent.Phase ?? "PHASE").ToUpperInvariant(),
                ConsoleColor.DarkCyan
            ),
            ProbeLogEventKind.ProgressTick => ("LIVE", ConsoleColor.Gray),
            ProbeLogEventKind.ProbeCompleted => ("PASS", ConsoleColor.Green),
            ProbeLogEventKind.BudgetViolated => ("FAIL", ConsoleColor.Red),
            ProbeLogEventKind.ArtifactsWritten => ("OUT", ConsoleColor.Blue),
            _ => ("INFO", ConsoleColor.White),
        };

        string prefix = string.IsNullOrEmpty(logEvent.ProbeName)
            ? $"[{tag}]"
            : $"[{tag}] [{logEvent.ProbeName}]";

        lock (_sync)
        {
            if (_useColor)
            {
                ConsoleColor prev = Console.ForegroundColor;
                Console.ForegroundColor = color;
                _writer.Write(prefix);
                Console.ForegroundColor = prev;
                _writer.WriteLine($" {logEvent.Message}");
            }
            else
            {
                _writer.WriteLine($"{prefix} {logEvent.Message}");
            }
        }
    }
}
