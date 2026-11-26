using System.Runtime.CompilerServices;
using Platform.Time;

namespace MacroBench.Pacing;

/// <summary>
/// High-resolution open-loop event pacer operating on <see cref="NativeTsc.NowNs"/>.
/// Eliminates Coordinated Omission by returning the intended scheduled timestamp rather than post-spin wall time,
/// and tracks schedule slips when the producer falls behind the target cadence.
/// </summary>
public struct OpenLoopPacer
{
    private readonly PacerMode _mode;
    private readonly double _nsPerOp;
    private long _startNs;
    private long _opIndex;
    private long _scheduleSlips;

    /// <summary>Active pacing mode.</summary>
    public PacerMode Mode => _mode;

    /// <summary>Configured target throughput in operations per second (0 when saturated).</summary>
    public long TargetOpsPerSec { get; }

    /// <summary>Total dispatch slots issued since construction or <see cref="Reset"/>.</summary>
    public long OperationsIssued => _opIndex;

    /// <summary>Number of slots where the producer fell more than one interval behind schedule.</summary>
    public long ScheduleSlips => _scheduleSlips;

    /// <summary>Initializes a paced open-loop pacer targeting <paramref name="targetOpsPerSec"/>.</summary>
    public OpenLoopPacer(long targetOpsPerSec)
        : this(PacerMode.Paced, targetOpsPerSec) { }

    /// <summary>Initializes an open-loop pacer in the specified <paramref name="mode"/>.</summary>
    public OpenLoopPacer(PacerMode mode, long targetOpsPerSec = 0)
    {
        _mode = mode;
        TargetOpsPerSec = targetOpsPerSec;

        if (mode == PacerMode.Paced)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetOpsPerSec);
            _nsPerOp = 1_000_000_000.0 / targetOpsPerSec;
        }
        else
        {
            _nsPerOp = 0.0;
        }

        _startNs = NativeTsc.NowNs();
        _opIndex = 0;
        _scheduleSlips = 0;
    }

    /// <summary>Creates an unthrottled pacer running at maximum hardware rate.</summary>
    public static OpenLoopPacer CreateSaturated() => new(PacerMode.Saturated);

    /// <summary>Creates a constant-rate open-loop pacer targeting <paramref name="targetOpsPerSec"/>.</summary>
    public static OpenLoopPacer CreatePaced(long targetOpsPerSec) =>
        new(PacerMode.Paced, targetOpsPerSec);

    /// <summary>
    /// Waits until the next scheduled dispatch slot (in <see cref="PacerMode.Paced"/> mode)
    /// and returns the scheduled UTC timestamp in nanoseconds for payload latency tagging.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long WaitNext()
    {
        if (_mode == PacerMode.Saturated)
        {
            _opIndex++;
            return NativeTsc.NowNs();
        }

        long targetNs = _startNs + (long)(_opIndex * _nsPerOp);
        long nowNs = NativeTsc.NowNs();

        if (nowNs > targetNs + (long)_nsPerOp)
        {
            _scheduleSlips++;
        }

        while (nowNs < targetNs)
        {
            Thread.SpinWait(2);
            nowNs = NativeTsc.NowNs();
        }

        _opIndex++;
        return targetNs;
    }

    /// <summary>
    /// Non-blocking check: returns true if the current scheduled slot has arrived and outputs its scheduled timestamp.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire(out long scheduledNs)
    {
        if (_mode == PacerMode.Saturated)
        {
            _opIndex++;
            scheduledNs = NativeTsc.NowNs();
            return true;
        }

        long targetNs = _startNs + (long)(_opIndex * _nsPerOp);
        long nowNs = NativeTsc.NowNs();

        if (nowNs >= targetNs)
        {
            if (nowNs > targetNs + (long)_nsPerOp)
            {
                _scheduleSlips++;
            }

            _opIndex++;
            scheduledNs = targetNs;
            return true;
        }

        scheduledNs = 0;
        return false;
    }

    /// <summary>
    /// Resets the schedule origin, operation counter, and slip count to the current timestamp.
    /// </summary>
    public void Reset()
    {
        _startNs = NativeTsc.NowNs();
        _opIndex = 0;
        _scheduleSlips = 0;
    }
}
