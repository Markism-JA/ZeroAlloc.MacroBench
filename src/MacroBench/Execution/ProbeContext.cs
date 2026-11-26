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

    public HostEnvironmentInfo Host { get; }
    public TimeSpan WarmupDuration { get; }
    public TimeSpan MeasureDuration { get; }
    public CancellationToken CancellationToken { get; }
    public NativeHistogram Histogram => _histogram;
    public NativeHistogramHandle HistogramHandle => _histogram.Handle;
    public LiveProgressBlock* LiveProgressPointer => _liveProgress;

    public long Operations => Volatile.Read(ref _operations);
    public long DroppedOperations => Volatile.Read(ref _droppedOperations);
    public long BytesTransferred => Volatile.Read(ref _bytesTransferred);
    public long ScheduleSlips => Volatile.Read(ref _scheduleSlips);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatencyNs(long elapsedNs) => _histogram.Record(elapsedNs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordLatencyNsAtomic(long elapsedNs) => _histogram.RecordAtomic(elapsedNs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddOperations(long count = 1) => Interlocked.Add(ref _operations, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetOperations(long total) => Volatile.Write(ref _operations, total);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddDrops(long count = 1) => Interlocked.Add(ref _droppedOperations, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetDrops(long total) => Volatile.Write(ref _droppedOperations, total);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddBytes(long bytes) => Interlocked.Add(ref _bytesTransferred, bytes);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetScheduleSlips(long slips) => Volatile.Write(ref _scheduleSlips, slips);

    /// <summary>
    /// Publishes current progress counters via the lock-free Seqlock block for live observer polling.
    /// </summary>
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

    /// <summary>
    /// Attaches a custom numeric metric (e.g., failover cutover latency) to the final report.
    /// </summary>
    public void SetCustomMetric(string key, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        (_customMetrics ??= new Dictionary<string, double>(StringComparer.Ordinal))[key] = value;
    }

    /// <summary>
    /// Resets histogram and counters between Warmup and Measure phases.
    /// </summary>
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
