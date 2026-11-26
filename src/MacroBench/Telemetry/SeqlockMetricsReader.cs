using System.Runtime.CompilerServices;

namespace MacroBench.Telemetry;

/// <summary>
/// Common interface for unmanaged metric structures protected by a 32-bit sequence lock.
/// </summary>
public interface ISeqlockMetricBlock
{
    public uint Sequence { get; set; }
}

/// <summary>
/// Lock-free, zero-allocation Seqlock reader and writer for live progress snapshots across threads.
/// Includes explicit memory barriers for ARM64 weak memory ordering.
/// </summary>
public static unsafe class SeqlockMetricsReader
{
    private const int DefaultMaxSpinAttempts = 100_000;

    /// <summary>
    /// Attempts to atomically read an unmanaged Seqlock-protected block without blocking writers.
    /// Returns <see langword="false"/> if the source pointer is null or the spin budget is exhausted.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryRead<T>(
        T* pSource,
        out T value,
        int maxSpinAttempts = DefaultMaxSpinAttempts
    )
        where T : unmanaged
    {
        if (pSource == null)
        {
            value = default;
            return false;
        }

        T result = default;
        var spin = new SpinWait();
        int attempts = 0;
        uint* pVersion = (uint*)pSource;

        while (attempts++ < maxSpinAttempts)
        {
            uint v1 = Volatile.Read(ref *pVersion);

            if ((v1 & 1) != 0)
            {
                spin.SpinOnce();
                continue;
            }

            Unsafe.CopyBlockUnaligned(
                destination: Unsafe.AsPointer(ref result),
                source: pSource,
                byteCount: (uint)sizeof(T)
            );

            // Prevent payload reads from reordering past the second version check on ARM64
            Thread.MemoryBarrier();

            uint v2 = Volatile.Read(ref *pVersion);
            if (v1 == v2)
            {
                value = result;
                return true;
            }

            spin.SpinOnce();
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Atomically reads an unmanaged Seqlock-protected block without blocking writers,
    /// falling back to a direct copy if <paramref name="maxSpinAttempts"/> is exceeded.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Read<T>(T* pSource, int maxSpinAttempts = DefaultMaxSpinAttempts)
        where T : unmanaged
    {
        if (TryRead(pSource, out T value, maxSpinAttempts))
            return value;

        if (pSource == null)
            return default;

        T fallback = default;
        Unsafe.CopyBlockUnaligned(
            destination: Unsafe.AsPointer(ref fallback),
            source: pSource,
            byteCount: (uint)sizeof(T)
        );
        return fallback;
    }

    /// <summary>
    /// Writes a complete metric snapshot using the Seqlock protocol.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<T>(T* pDestination, in T value)
        where T : unmanaged
    {
        if (pDestination == null)
            return;

        uint* pVersion = (uint*)pDestination;
        uint seq = *pVersion;

        // 1. Mark mutating (odd)
        Volatile.Write(ref *pVersion, seq + 1);
        Thread.MemoryBarrier();

        // 2. Copy payload (excluding the leading 4-byte sequence header)
        byte* pDstPayload = (byte*)pDestination + sizeof(uint);
        fixed (T* pSrc = &value)
        {
            byte* pSrcPayload = (byte*)pSrc + sizeof(uint);
            Unsafe.CopyBlockUnaligned(
                destination: pDstPayload,
                source: pSrcPayload,
                byteCount: (uint)(sizeof(T) - sizeof(uint))
            );
        }

        // 3. Ensure all payload stores are globally visible before publishing the even sequence
        Thread.MemoryBarrier();
        Volatile.Write(ref *pVersion, seq + 2);
    }
}
