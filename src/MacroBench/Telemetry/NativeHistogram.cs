using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MacroBench.Telemetry;

/// <summary>
/// Unmanaged state header + bucket storage for <see cref="NativeHistogram"/>.
/// 30 octaves * 128 sub-buckets = 3,840 buckets (~30.7 KB total, L1/L2 cache-resident).
/// Exact 1 ns resolution up to 255 ns; &lt; 0.8% relative error up to ~68.7 seconds.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct NativeHistogramState
{
    public const int SubBucketCount = 128;
    public const int OctaveCount = 30;
    public const int TotalBuckets = OctaveCount * SubBucketCount; // 3,840
    public const long MaxTrackedNs = ((128L + 127L + 1L) << 28) - 1L; // ~68.7s

    public long TotalCount;
    public long SumNs;
    public long MinNs;
    public long MaxNs;
    public fixed long Buckets[TotalBuckets];
}

/// <summary>
/// Blittable 8-byte pointer wrapper for recording into a <see cref="NativeHistogram"/>
/// directly from unmanaged <c>delegate* unmanaged</c> callbacks without <see cref="GCHandle"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct NativeHistogramHandle(NativeHistogramState* state)
{
    private readonly NativeHistogramState* _state = state;

    public bool IsValid => _state != null;

    /// <summary>Records a latency sample in nanoseconds on a single-writer hot path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long elapsedNs)
    {
        if (elapsedNs < 0)
            elapsedNs = 0;
        else if (elapsedNs > NativeHistogramState.MaxTrackedNs)
            elapsedNs = NativeHistogramState.MaxTrackedNs;

        int idx = GetBucketIndex(elapsedNs);
        _state->Buckets[idx]++;
        _state->TotalCount++;
        _state->SumNs += elapsedNs;

        if (elapsedNs < _state->MinNs)
            _state->MinNs = elapsedNs;
        if (elapsedNs > _state->MaxNs)
            _state->MaxNs = elapsedNs;
    }

    /// <summary>Thread-safe multi-producer sample recording.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordAtomic(long elapsedNs)
    {
        if (elapsedNs < 0)
            elapsedNs = 0;
        else if (elapsedNs > NativeHistogramState.MaxTrackedNs)
            elapsedNs = NativeHistogramState.MaxTrackedNs;

        int idx = GetBucketIndex(elapsedNs);
        Interlocked.Increment(ref _state->Buckets[idx]);
        Interlocked.Increment(ref _state->TotalCount);
        Interlocked.Add(ref _state->SumNs, elapsedNs);

        long currentMin;
        while (elapsedNs < (currentMin = Volatile.Read(ref _state->MinNs)))
        {
            if (Interlocked.CompareExchange(ref _state->MinNs, elapsedNs, currentMin) == currentMin)
                break;
        }

        long currentMax;
        while (elapsedNs > (currentMax = Volatile.Read(ref _state->MaxNs)))
        {
            if (Interlocked.CompareExchange(ref _state->MaxNs, elapsedNs, currentMax) == currentMax)
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetBucketIndex(long value)
    {
        if (value < 256)
            return (int)value;

        int msb = 63 - BitOperations.LeadingZeroCount((ulong)value);
        int shift = msb - 7;
        int octave = shift + 1;
        if (octave >= NativeHistogramState.OctaveCount)
            return NativeHistogramState.TotalBuckets - 1;

        int sub = (int)(value >> shift) - NativeHistogramState.SubBucketCount;
        return (octave << 7) + sub;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long GetBucketUpperBoundNs(int bucketIndex)
    {
        if (bucketIndex < 256)
            return bucketIndex;

        int octave = bucketIndex >> 7;
        int sub = bucketIndex & 127;
        int shift = octave - 1;
        return ((128L + sub + 1L) << shift) - 1L;
    }
}

/// <summary>
/// Cache-resident, 64-byte-aligned unmanaged HDR log-linear histogram (~30.7 KB).
/// Exposes <see cref="Handle"/> for zero-allocation native callback recording and computes
/// all percentiles in a single O(3840) scan.
/// </summary>
public sealed unsafe class NativeHistogram : IDisposable
{
    private NativeHistogramState* _state;
    private int _isDisposed;

    public NativeHistogram()
    {
        _state = (NativeHistogramState*)
            NativeMemory.AlignedAlloc((nuint)sizeof(NativeHistogramState), 64);
        Reset();
    }

    public NativeHistogramHandle Handle => new(_state);
    public long TotalCount => _state == null ? 0 : Volatile.Read(ref _state->TotalCount);
    public long MinNs
    {
        get
        {
            if (_state == null)
                return 0;
            long min = Volatile.Read(ref _state->MinNs);
            return min == long.MaxValue ? 0 : min;
        }
    }
    public long MaxNs => _state == null ? 0 : Volatile.Read(ref _state->MaxNs);
    public double MeanNs
    {
        get
        {
            if (_state == null)
                return 0;
            long count = Volatile.Read(ref _state->TotalCount);
            return count == 0 ? 0 : (double)Volatile.Read(ref _state->SumNs) / count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long elapsedNs) => Handle.Record(elapsedNs);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordAtomic(long elapsedNs) => Handle.RecordAtomic(elapsedNs);

    /// <summary>
    /// Merges all samples from <paramref name="other"/> into this histogram (for combining per-worker histograms).
    /// </summary>
    public void MergeFrom(NativeHistogram other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_state == null || other._state == null || other._state->TotalCount == 0)
            return;

        _state->TotalCount += other._state->TotalCount;
        _state->SumNs += other._state->SumNs;
        if (other._state->MinNs < _state->MinNs)
            _state->MinNs = other._state->MinNs;
        if (other._state->MaxNs > _state->MaxNs)
            _state->MaxNs = other._state->MaxNs;

        for (int i = 0; i < NativeHistogramState.TotalBuckets; i++)
        {
            long c = other._state->Buckets[i];
            if (c != 0)
                _state->Buckets[i] += c;
        }
    }

    /// <summary>
    /// Captures all percentiles in a single pass over the 3,840 cache-resident buckets.
    /// </summary>
    public HistogramSnapshot Snapshot()
    {
        if (_state == null)
            return default;

        long total = Volatile.Read(ref _state->TotalCount);
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
        long maxRecorded = MaxNs;

        for (int i = 0; i < NativeHistogramState.TotalBuckets; i++)
        {
            long count = _state->Buckets[i];
            if (count == 0)
                continue;

            acc += count;
            long val = Math.Min(NativeHistogramHandle.GetBucketUpperBoundNs(i), maxRecorded);

            if (!f50 && acc >= t50) { p50 = val; f50 = true; }
            if (!f90 && acc >= t90) { p90 = val; f90 = true; }
            if (!f95 && acc >= t95) { p95 = val; f95 = true; }
            if (!f99 && acc >= t99) { p99 = val; f99 = true; }
            if (!f999 && acc >= t999) { p999 = val; f999 = true; }
            if (!f9999 && acc >= t9999) { p9999 = val; f9999 = true; break; }
        }

        return new HistogramSnapshot(
            TotalCount: total,
            MinNs: MinNs,
            MeanNs: MeanNs,
            P50Ns: p50,
            P90Ns: p90,
            P99Ns: p99,
            P999Ns: p999,
            MaxNs: maxRecorded,
            P95Ns: p95,
            P9999Ns: p9999
        );
    }

    public void Reset()
    {
        if (_state == null)
            return;

        NativeMemory.Clear(_state, (nuint)sizeof(NativeHistogramState));
        _state->MinNs = long.MaxValue;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        if (_state != null)
        {
            NativeMemory.AlignedFree(_state);
            _state = null;
        }
    }
}
