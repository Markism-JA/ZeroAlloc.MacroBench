using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MacroBench.Environment;
using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Execution context passed to <see cref="IMacroProbe"/> lifecycle hooks.
/// Provides zero-allocation histogram recording, Seqlock live progress publication, and counter aggregation.
/// </summary>
public sealed unsafe class ProbeContext : IDisposable
{
    private readonly NativeHistogram _histogram;
    private LiveProgressBlock* _liveProgress;
    private Dictionary<string, double>? _customMetrics;
    private long _operations;
    private long _droppedOperations;
    private long _bytesTransferred;
    private long _scheduleSlips;
    private int _isDisposed;

    /// <summary>Captured host hardware and runtime metadata.</summary>
    public HostEnvironmentInfo Host { get; }

    /// <summary>Configured warmup duration hint.</summary>
    public TimeSpan WarmupDuration { get; }

    /// <summary>Configured measurement duration hint.</summary>
    public TimeSpan MeasureDuration { get; }

    /// <summary>Cancellation token signaled if the run is aborted.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Cache-resident unmanaged HDR histogram for latency recording.</summary>
    public NativeHistogram Histogram => _histogram;

    /// <summary>Blittable 8-byte handle for recording into <see cref="Histogram"/> from native callbacks.</summary>
    public NativeHistogramHandle HistogramHandle => _histogram.Handle;

    /// <summary>Pointer to the 64-byte-aligned Seqlock live progress block.</summary>
    public LiveProgressBlock* LiveProgressPointer => _liveProgress;

    /// <summary>Total operations recorded in the current phase.</summary>
    public long Operations => Volatile.Read(ref _operations);

    /// <summary>Total dropped operations recorded in the current phase.</summary>
    public long DroppedOperations => Volatile.Read(ref _droppedOperations);

    /// <summary>Total bytes transferred in the current phase.</summary>
    public long BytesTransferred => Volatile.Read(ref _bytesTransferred);

    /// <summary>Total open-loop schedule slips recorded in the current phase.</summary>
    public long ScheduleSlips => Volatile.Read(ref _scheduleSlips);

    /// <summary>Optional custom numeric metrics attached to the probe report.</summary>
    public IReadOnlyDictionary<string, double>? CustomMetrics => _customMetrics;

    internal ProbeContext(
        HostEnvironmentInfo host,
        TimeSpan warmupDuration,
        TimeSpan measureDuration,
        CancellationToken cancellationToken
    )
    {
        Host = host;
        WarmupDuration = warmupDuration;
        MeasureDuration = measureDuration;
        CancellationToken = cancellationToken;
        _histogram = new NativeHistogram();
        _liveProgress = (LiveProgressBlock*)
            NativeMemory.AlignedAlloc((nuint)sizeof(LiveProgressBlock), 64);
        NativeMemory.Clear(_liveProgress, (nuint)sizeof(LiveProgressBlock));
    }

    /// <summary>Records a latency sample in nanoseconds on a single-writer hot path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatencyNs(long elapsedNs) => _histogram.Record(elapsedNs);

    /// <summary>Thread-safe multi-producer latency recording in nanoseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatencyNsAtomic(long elapsedNs) => _histogram.RecordAtomic(elapsedNs);

    /// <summary>Atomically increments the operation counter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddOperations(long count = 1) => Interlocked.Add(ref _operations, count);

    /// <summary>Sets the total operation counter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOperations(long total) => Volatile.Write(ref _operations, total);

    /// <summary>Atomically increments the dropped-operation counter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddDrops(long count = 1) => Interlocked.Add(ref _droppedOperations, count);

    /// <summary>Sets the total dropped-operation counter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetDrops(long total) => Volatile.Write(ref _droppedOperations, total);

    /// <summary>Atomically increments the transferred-byte counter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddBytes(long bytes) => Interlocked.Add(ref _bytesTransferred, bytes);

    /// <summary>Sets the total schedule-slip counter.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetScheduleSlips(long slips) => Volatile.Write(ref _scheduleSlips, slips);

    /// <summary>Publishes current progress counters via the lock-free Seqlock block for live observer polling.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PublishLiveProgress(long operations, long droppedOperations = 0, long bytesTransferred = 0, long scheduleSlips = 0)
    {
        if (_liveProgress == null)
            return;

        var block = new LiveProgressBlock
        {
            Operations = operations,
            DroppedOperations = droppedOperations,
            BytesTransferred = bytesTransferred,
            ScheduleSlips = scheduleSlips,
        };
        SeqlockMetricsReader.Write(_liveProgress, in block);
    }

    /// <summary>Attaches a custom numeric metric (e.g., failover cutover latency) to the final report.</summary>
    public void SetCustomMetric(string key, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        (_customMetrics ??= new Dictionary<string, double>(StringComparer.Ordinal))[key] = value;
    }

    internal void ResetForMeasurement()
    {
        _histogram.Reset();
        Volatile.Write(ref _operations, 0);
        Volatile.Write(ref _droppedOperations, 0);
        Volatile.Write(ref _bytesTransferred, 0);
        Volatile.Write(ref _scheduleSlips, 0);
        _customMetrics?.Clear();

        if (_liveProgress != null)
        {
            NativeMemory.Clear(_liveProgress, (nuint)sizeof(LiveProgressBlock));
        }
    }

    /// <summary>Releases unmanaged histogram and Seqlock progress memory.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        _histogram.Dispose();
        if (_liveProgress != null)
        {
            NativeMemory.AlignedFree(_liveProgress);
            _liveProgress = null;
        }
    }
}
