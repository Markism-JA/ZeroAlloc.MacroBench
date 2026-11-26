using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MacroBench.Telemetry;

/// <summary>
/// Cache-aligned, 0-allocation unmanaged exact linear histogram (1 ns per bucket).
/// Prefer <see cref="NativeHistogram"/> when tracking multi-millisecond ranges per worker thread.
/// </summary>
public sealed unsafe class UnmanagedLinearHistogram : IDisposable
{
    private readonly int _maxTrackedNs;
    private readonly nuint _allocationSizeBytes;
    private long* _buckets;

    private long _totalCount;
    private long _sumNs;
    private long _minNs = long.MaxValue;
    private long _maxNs;
    private int _isDisposed;

    /// <summary>Allocates a 64-byte-aligned linear bucket array covering <c>0..maxTrackedNs</c>.</summary>
    public UnmanagedLinearHistogram(int maxTrackedNs = 20_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTrackedNs);

        _maxTrackedNs = maxTrackedNs;
        _allocationSizeBytes = (nuint)((maxTrackedNs + 1) * sizeof(long));
        _buckets = (long*)NativeMemory.AlignedAlloc(_allocationSizeBytes, 64);
        Reset();
    }

    /// <summary>Maximum tracked latency in nanoseconds.</summary>
    public int MaxTrackedNs => _maxTrackedNs;

    /// <summary>Total recorded sample count.</summary>
    public long TotalCount => Volatile.Read(ref _totalCount);

    /// <summary>Minimum recorded sample in nanoseconds (0 when empty).</summary>
    public long MinNs => Volatile.Read(ref _minNs) == long.MaxValue ? 0 : Volatile.Read(ref _minNs);

    /// <summary>Maximum recorded sample in nanoseconds.</summary>
    public long MaxNs => Volatile.Read(ref _maxNs);

    /// <summary>Arithmetic mean of recorded samples in nanoseconds.</summary>
    public double MeanNs
    {
        get
        {
            long count = Volatile.Read(ref _totalCount);
            return count == 0 ? 0 : (double)Volatile.Read(ref _sumNs) / count;
        }
    }

    /// <summary>Records a latency sample in nanoseconds on a single-writer hot path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long elapsedNs)
    {
        if (elapsedNs < 0)
            elapsedNs = 0;
        else if (elapsedNs > _maxTrackedNs)
            elapsedNs = _maxTrackedNs;

        _buckets[elapsedNs]++;
        _totalCount++;
        _sumNs += elapsedNs;

        if (elapsedNs < _minNs)
            _minNs = elapsedNs;
        if (elapsedNs > _maxNs)
            _maxNs = elapsedNs;
    }

    /// <summary>Thread-safe multi-producer sample recording in nanoseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordAtomic(long elapsedNs)
    {
        if (elapsedNs < 0)
            elapsedNs = 0;
        else if (elapsedNs > _maxTrackedNs)
            elapsedNs = _maxTrackedNs;

        Interlocked.Increment(ref _buckets[elapsedNs]);
        Interlocked.Increment(ref _totalCount);
        Interlocked.Add(ref _sumNs, elapsedNs);

        long currentMin;
        while (elapsedNs < (currentMin = Volatile.Read(ref _minNs)))
        {
            if (Interlocked.CompareExchange(ref _minNs, elapsedNs, currentMin) == currentMin)
                break;
        }

        long currentMax;
        while (elapsedNs > (currentMax = Volatile.Read(ref _maxNs)))
        {
            if (Interlocked.CompareExchange(ref _maxNs, elapsedNs, currentMax) == currentMax)
                break;
        }
    }

    /// <summary>Computes the latency in nanoseconds at the requested <paramref name="percentile"/> (0..100).</summary>
    public long GetPercentile(double percentile)
    {
        long total = Volatile.Read(ref _totalCount);
        if (total == 0)
            return 0;

        long target = (long)Math.Ceiling((percentile / 100.0) * total);
        long accumulated = 0;

        for (int i = 0; i <= _maxTrackedNs; i++)
        {
            accumulated += _buckets[i];
            if (accumulated >= target)
                return i;
        }

        return _maxTrackedNs;
    }

    /// <summary>Captures all percentiles in a single pass over the buckets.</summary>
    public HistogramSnapshot Snapshot()
    {
        long total = Volatile.Read(ref _totalCount);
        if (total == 0)
            return default;

        long t50 = (long)Math.Ceiling(0.50 * total);
        long t90 = (long)Math.Ceiling(0.90 * total);
        long t95 = (long)Math.Ceiling(0.95 * total);
        long t99 = (long)Math.Ceiling(0.99 * total);
        long t999 = (long)Math.Ceiling(0.999 * total);
        long t9999 = (long)Math.Ceiling(0.9999 * total);

        long p50 = 0, p90 = 0, p95 = 0, p99 = 0, p999 = 0, p9999 = 0;
        bool f50 = false, f90 = false, f95 = false, f99 = false, f999 = false, f9999 = false;
        long acc = 0;

        for (int i = 0; i <= _maxTrackedNs; i++)
        {
            long count = _buckets[i];
            if (count == 0)
                continue;

            acc += count;
            if (!f50 && acc >= t50) { p50 = i; f50 = true; }
            if (!f90 && acc >= t90) { p90 = i; f90 = true; }
            if (!f95 && acc >= t95) { p95 = i; f95 = true; }
            if (!f99 && acc >= t99) { p99 = i; f99 = true; }
            if (!f999 && acc >= t999) { p999 = i; f999 = true; }
            if (!f9999 && acc >= t9999) { p9999 = i; f9999 = true; break; }
        }

        return new HistogramSnapshot(
            TotalCount: total,
            MinNs: MinNs,
            MeanNs: MeanNs,
            P50Ns: p50,
            P90Ns: p90,
            P99Ns: p99,
            P999Ns: p999,
            MaxNs: MaxNs,
            P95Ns: p95,
            P9999Ns: p9999
        );
    }

    /// <summary>Clears all buckets and resets summary statistics.</summary>
    public void Reset()
    {
        NativeMemory.Clear(_buckets, _allocationSizeBytes);
        _totalCount = 0;
        _sumNs = 0;
        _minNs = long.MaxValue;
        _maxNs = 0;
    }

    /// <summary>Frees the unmanaged bucket array.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        if (_buckets != null)
        {
            NativeMemory.AlignedFree(_buckets);
            _buckets = null;
        }
    }
}

/// <summary>Immutable percentile and summary statistics captured from a histogram.</summary>
/// <param name="TotalCount">Total recorded sample count.</param>
/// <param name="MinNs">Minimum latency in nanoseconds.</param>
/// <param name="MeanNs">Mean latency in nanoseconds.</param>
/// <param name="P50Ns">50th percentile (median) latency in nanoseconds.</param>
/// <param name="P90Ns">90th percentile latency in nanoseconds.</param>
/// <param name="P99Ns">99th percentile latency in nanoseconds.</param>
/// <param name="P999Ns">99.9th percentile latency in nanoseconds.</param>
/// <param name="MaxNs">Maximum latency in nanoseconds.</param>
/// <param name="P95Ns">95th percentile latency in nanoseconds.</param>
/// <param name="P9999Ns">99.99th percentile latency in nanoseconds.</param>
public readonly record struct HistogramSnapshot(
    long TotalCount,
    long MinNs,
    double MeanNs,
    long P50Ns,
    long P90Ns,
    long P99Ns,
    long P999Ns,
    long MaxNs,
    long P95Ns = 0,
    long P9999Ns = 0
);
