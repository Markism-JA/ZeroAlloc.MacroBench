namespace MacroBench.Telemetry;

/// <summary>
/// Managed heap allocation and garbage collection counts recorded over a measurement window.
/// </summary>
public readonly record struct GcDelta(
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections
);

/// <summary>
/// Lightweight struct capturing precise managed allocation and GC collection deltas.
/// </summary>
public readonly struct GcDeltaTracker
{
    private readonly long _startAllocatedBytes;
    private readonly int _startGen0;
    private readonly int _startGen1;
    private readonly int _startGen2;

    private GcDeltaTracker(long allocatedBytes, int gen0, int gen1, int gen2)
    {
        _startAllocatedBytes = allocatedBytes;
        _startGen0 = gen0;
        _startGen1 = gen1;
        _startGen2 = gen2;
    }

    /// <summary>Captures starting allocation and GC collection counts.</summary>
    public static GcDeltaTracker Start() =>
        new(
            GC.GetTotalAllocatedBytes(precise: true),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2)
        );

    /// <summary>Computes the allocation and GC collection deltas since <see cref="Start"/>.</summary>
    public GcDelta Stop() =>
        new(
            AllocatedBytes: Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - _startAllocatedBytes),
            Gen0Collections: Math.Max(0, GC.CollectionCount(0) - _startGen0),
            Gen1Collections: Math.Max(0, GC.CollectionCount(1) - _startGen1),
            Gen2Collections: Math.Max(0, GC.CollectionCount(2) - _startGen2)
        );
}
