using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace MacroBench.Environment;

/// <summary>
/// Immutable snapshot of host hardware, OS, and .NET runtime characteristics captured once per benchmark run.
/// </summary>
public sealed record HostEnvironmentInfo(
    string OsDescription,
    string OsArchitecture,
    string ProcessArchitecture,
    int LogicalCoreCount,
    string RuntimeVersion,
    bool IsServerGC,
    string ProcessPriority,
    string? ThrottlingWarning,
    DateTimeOffset CapturedAtUtc
)
{
    public static HostEnvironmentInfo Capture()
    {
        string priority;
        try
        {
            priority = Process.GetCurrentProcess().PriorityClass.ToString();
        }
        catch
        {
            priority = "Unknown";
        }

        return new HostEnvironmentInfo(
            OsDescription: RuntimeInformation.OSDescription.Trim(),
            OsArchitecture: RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            LogicalCoreCount: System.Environment.ProcessorCount,
            RuntimeVersion: RuntimeInformation.FrameworkDescription.Trim(),
            IsServerGC: GCSettings.IsServerGC,
            ProcessPriority: priority,
            ThrottlingWarning: ProcessPriorityGuard.DetectThrottlingWarning(),
            CapturedAtUtc: DateTimeOffset.UtcNow
        );
    }

    public string ToShortSummary() =>
        $"{RuntimeVersion} ({ProcessArchitecture}, {LogicalCoreCount} logical cores, {(IsServerGC ? "ServerGC" : "WorkstationGC")}) on {OsDescription}";
}
