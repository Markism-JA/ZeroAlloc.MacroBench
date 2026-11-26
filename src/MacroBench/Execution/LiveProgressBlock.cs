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
    public uint Sequence { get; set; }
    public long Operations;
    public long DroppedOperations;
    public long BytesTransferred;
    public long ScheduleSlips;
}
