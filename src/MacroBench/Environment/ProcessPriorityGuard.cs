using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MacroBench.Environment;

/// <summary>
/// Best-effort process priority elevation and OS power/throttling diagnostics.
/// </summary>
public static class ProcessPriorityGuard
{
    /// <summary>Attempts to elevate the current process priority class without throwing.</summary>
    public static bool TryElevate(ProcessPriorityClass preferred = ProcessPriorityClass.High)
    {
        try
        {
            Process current = Process.GetCurrentProcess();
            try
            {
                current.PriorityClass = preferred;
                return true;
            }
            catch when (preferred == ProcessPriorityClass.RealTime)
            {
                current.PriorityClass = ProcessPriorityClass.High;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Inspects the host OS power scheme/governor and returns a warning message if frequency scaling
    /// or power saving modes may contaminate measurements, or <see langword="null"/> if healthy.
    /// </summary>
    public static string? DetectThrottlingWarning()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return InspectLinuxGovernor();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return InspectWindowsPowerScheme();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return InspectMacOsThrottling();
        }
        catch
        {
            // Non-critical diagnostic inspection must never throw
        }

        return null;
    }

    /// <summary>
    /// Inspects the host OS power scheme/governor and writes a console warning if throttled.
    /// </summary>
    public static void WarnIfThrottled()
    {
        string? warning = DetectThrottlingWarning();
        if (warning is null)
            return;

        bool useColor = !Console.IsOutputRedirected && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("NO_COLOR"));
        if (useColor)
            Console.ForegroundColor = ConsoleColor.Yellow;

        Console.WriteLine($"[WARN] {warning}");

        if (useColor)
            Console.ResetColor();
    }

    private static string? InspectLinuxGovernor()
    {
        const string governorPath = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor";
        if (!File.Exists(governorPath))
            return null;

        string governor = File.ReadAllText(governorPath).Trim();
        if (!string.Equals(governor, "performance", StringComparison.OrdinalIgnoreCase))
        {
            return $"Linux CPU scaling governor is '{governor}'. "
                + "For deterministic results, run: 'echo performance | sudo tee /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor'";
        }

        return null;
    }

    private static string? InspectWindowsPowerScheme()
    {
        Guid highPerfGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        Guid ultimatePerfGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

        nint pActiveGuid = 0;
        try
        {
            uint status = Powrprof.PowerGetActiveScheme(0, out pActiveGuid);
            if (status == 0 && pActiveGuid != 0)
            {
                Guid activeGuid = Marshal.PtrToStructure<Guid>(pActiveGuid);
                if (activeGuid != highPerfGuid && activeGuid != ultimatePerfGuid)
                {
                    return $"Windows power scheme is Balanced/Power Saver ({activeGuid}). "
                        + "Enable 'High Performance' or 'Ultimate Performance' via powercfg for deterministic results.";
                }
            }
        }
        finally
        {
            if (pActiveGuid != 0)
                Kernel32.LocalFree(pActiveGuid);
        }

        return null;
    }

    private static string? InspectMacOsThrottling()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/pmset",
                Arguments = "-g",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        if (process.Start())
        {
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(500);

            if (output.Contains("lowpowermode 1", StringComparison.OrdinalIgnoreCase))
            {
                return "macOS Low Power Mode is active; CPU clocks and throughput will be throttled.";
            }
        }

        return null;
    }
}
