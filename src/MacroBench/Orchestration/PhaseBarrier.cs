using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Platform.Threading;

namespace MacroBench.Orchestration;

/// <summary>
/// Cache-aligned, 0-allocation synchronization barrier for multi-core probe rendezvous.
/// Uses progressive spin-wait followed by <see cref="ThreadParker"/> two-phase kernel parking.
/// </summary>
public sealed unsafe class PhaseBarrier : IDisposable
{
    private const int SpinCountThreshold = 2048;

    private readonly int _participantCount;
    private readonly ThreadParker[] _parkers;
    private readonly NativeThreadParkerHandle* _nativeHandles;
    private readonly int* _stateBlock; // [0] = Count, [1] = Generation
    private int _isDisposed;

    /// <summary>Number of participating threads required to release the barrier.</summary>
    public int ParticipantCount => _participantCount;

    /// <summary>Allocates a cache-aligned barrier for <paramref name="participantCount"/> threads.</summary>
    public PhaseBarrier(int participantCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(participantCount);

        _participantCount = participantCount;

        _stateBlock = (int*)NativeMemory.AlignedAlloc(64, 64);
        NativeMemory.Clear(_stateBlock, 64);

        nuint handlesSize = (nuint)(participantCount * sizeof(NativeThreadParkerHandle));
        _nativeHandles = (NativeThreadParkerHandle*)NativeMemory.AllocZeroed(handlesSize);

        _parkers = new ThreadParker[participantCount];
        for (int i = 0; i < participantCount; i++)
        {
            var parker = new ThreadParker();
            _parkers[i] = parker;
            _nativeHandles[i] = parker.NativeHandle;
        }
    }

    /// <summary>
    /// Signals arrival and waits for all participant cores to reach this phase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SignalAndWait(int participantIndex)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(participantIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(participantIndex, _participantCount);

        int* pCount = &_stateBlock[0];
        int* pGen = &_stateBlock[1];

        int currentGen = Volatile.Read(ref *pGen);
        int arrived = Interlocked.Increment(ref *pCount);

        if (arrived == _participantCount)
        {
            // Last participant resets arrival count and advances generation under full fence
            Volatile.Write(ref *pCount, 0);
            Interlocked.Increment(ref *pGen);
            WakeAllParticipants();
            return;
        }

        var spinWait = new SpinWait();
        int spins = 0;
        ThreadParker parker = _parkers[participantIndex];

        while (Volatile.Read(ref *pGen) == currentGen && Volatile.Read(ref _isDisposed) == 0)
        {
            if (++spins < SpinCountThreshold)
            {
                spinWait.SpinOnce();
                continue;
            }

            // Two-phase handshake: arm before re-checking generation so no wakeups are missed
            if (parker.TryArm())
            {
                if (Volatile.Read(ref *pGen) != currentGen || Volatile.Read(ref _isDisposed) != 0)
                {
                    parker.Disarm();
                    break;
                }

                parker.Park(timeoutMs: 10);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WakeAllParticipants()
    {
        for (int i = 0; i < _participantCount; i++)
        {
            _nativeHandles[i].Unpark();
        }
    }

    /// <summary>Wakes any waiting participants and frees unmanaged barrier memory and parkers.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        WakeAllParticipants();

        if (_parkers != null)
        {
            for (int i = 0; i < _parkers.Length; i++)
            {
                _parkers[i]?.Dispose();
            }
        }

        if (_nativeHandles != null)
        {
            NativeMemory.Free(_nativeHandles);
        }

        if (_stateBlock != null)
        {
            NativeMemory.AlignedFree(_stateBlock);
        }
    }
}
