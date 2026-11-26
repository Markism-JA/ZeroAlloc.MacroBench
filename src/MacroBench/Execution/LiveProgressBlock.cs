using System.Runtime.InteropServices;
using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Unmanaged Seqlock-protected progress block updated by probes during <see cref="IMacroProbe.Measure"/>
/// and polled lock-free by <see cref="ProbeRunner"/> for live progress output.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LiveProgressBlock : ISeqlockMetricBlock
{
    /// <inheritdoc />
    public uint Sequence { get; set; }

    /// <summary>Total operations completed so far.</summary>
    public long Operations;

    /// <summary>Total dropped operations observed so far.</summary>
    public long DroppedOperations;

    /// <summary>Total payload bytes transferred so far.</summary>
    public long BytesTransferred;

    /// <summary>Total pacer schedule slips observed so far.</summary>
    public long ScheduleSlips;
}
