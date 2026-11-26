using System.Runtime.CompilerServices;
using Platform.Threading;

namespace MacroBench.Orchestration;

/// <summary>
/// Controls how <see cref="PinnedWorkerThread"/> applies OS CPU affinity.
/// </summary>
public enum WorkerPinningMode : byte
{
    /// <summary>Pin to the requested core when supported; fall back to unpinned on macOS or out-of-range cores.</summary>
    BestEffort = 0,

    /// <summary>Require OS core pinning; throw if the platform or core index does not support it.</summary>
    Strict = 1,

    /// <summary>Do not attempt OS core pinning.</summary>
    Unpinned = 2,
}

/// <summary>
/// Dedicated OS worker thread driven by a low-level <see cref="ThreadParker"/>
/// with configurable CPU core affinity and thread priority.
/// </summary>
public sealed class PinnedWorkerThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ThreadParker _parker;
    private readonly CancellationTokenSource _cts;
    private int _isDisposed;

    /// <summary>Logical name assigned to the worker OS thread.</summary>
    public string Name { get; }

    /// <summary>Requested zero-based logical CPU core index.</summary>
    public int CoreIndex { get; }

    /// <summary>True if OS CPU affinity pinning succeeded for this worker.</summary>
    public bool IsPinned { get; private set; }

    /// <summary>Dedicated in-process thread parker owned by this worker.</summary>
    public ThreadParker Parker => _parker;

    /// <summary>Blittable producer handle for issuing zero-allocation wakeups to this worker.</summary>
    public NativeThreadParkerHandle NativeHandle => _parker.NativeHandle;

    /// <summary>True while the underlying OS thread is still running.</summary>
    public bool IsAlive => _thread.IsAlive;

    private PinnedWorkerThread(
        string name,
        int coreIndex,
        Action<PinnedWorkerContext> workLoop,
        ThreadPriority priority,
        WorkerPinningMode pinningMode
    )
    {
        Name = name;
        CoreIndex = coreIndex;
        _parker = new ThreadParker();
        _cts = new CancellationTokenSource();

        Exception? startupError = null;
        using var initialized = new ManualResetEventSlim(false);

        _thread = new Thread(() =>
        {
            try
            {
                IsPinned = ApplyAffinity(coreIndex, pinningMode);
            }
            catch (Exception ex)
            {
                startupError = ex;
                initialized.Set();
                return;
            }

            var context = new PinnedWorkerContext(Name, CoreIndex, IsPinned, _parker, _cts.Token);
            initialized.Set();
            workLoop(context);
        })
        {
            Name = name,
            IsBackground = true,
            Priority = priority,
        };

        _thread.Start();
        initialized.Wait();

        if (startupError is not null)
        {
            _cts.Dispose();
            _parker.Dispose();
            throw startupError;
        }
    }

    /// <summary>
    /// Spawns a dedicated OS worker thread with optional hardware core pinning.
    /// </summary>
    public static PinnedWorkerThread Spawn(
        string name,
        int coreIndex,
        Action<PinnedWorkerContext> workLoop,
        ThreadPriority priority = ThreadPriority.Highest,
        WorkerPinningMode pinningMode = WorkerPinningMode.BestEffort
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workLoop);

        return new PinnedWorkerThread(name, coreIndex, workLoop, priority, pinningMode);
    }

    private static bool ApplyAffinity(int coreIndex, WorkerPinningMode mode)
    {
        if (mode == WorkerPinningMode.Unpinned || coreIndex < 0)
            return false;

        if (coreIndex >= System.Environment.ProcessorCount)
        {
            if (mode == WorkerPinningMode.Strict)
                throw new ArgumentOutOfRangeException(
                    nameof(coreIndex),
                    coreIndex,
                    $"Requested core {coreIndex} exceeds processor count {System.Environment.ProcessorCount}."
                );
            return false;
        }

        try
        {
            ThreadAffinityUtility.PinCurrentThreadToCore(coreIndex);
            return true;
        }
        catch (PlatformNotSupportedException) when (mode == WorkerPinningMode.BestEffort)
        {
            return false;
        }
    }

    /// <summary>Signals the worker's parker to wake it if parked.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unpark() => _parker.Unpark();

    /// <summary>Waits for the worker thread to exit naturally within <paramref name="timeout"/> without cancelling.</summary>
    public bool WaitForCompletion(TimeSpan timeout) => _thread.Join(timeout);

    /// <summary>Requests cancellation, unparks the worker, and waits for it to terminate.</summary>
    public void Join(TimeSpan timeout)
    {
        _cts.Cancel();
        _parker.Unpark();
        _thread.Join(timeout);
    }

    /// <summary>Cancels, unparks, joins, and disposes the worker thread's resources.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        _cts.Cancel();
        _parker.Unpark();

        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _cts.Dispose();
        _parker.Dispose();
    }
}

/// <summary>
/// Execution context passed into a <see cref="PinnedWorkerThread"/> loop.
/// </summary>
public readonly struct PinnedWorkerContext(
    string name,
    int coreIndex,
    bool isPinned,
    ThreadParker parker,
    CancellationToken cancellationToken
)
{
    /// <summary>Worker thread name.</summary>
    public string Name => name;

    /// <summary>Requested logical CPU core index.</summary>
    public int CoreIndex => coreIndex;

    /// <summary>True if OS CPU affinity pinning succeeded.</summary>
    public bool IsPinned => isPinned;

    /// <summary>Dedicated thread parker for this worker.</summary>
    public ThreadParker Parker => parker;

    /// <summary>Blittable unpark handle for producers.</summary>
    public NativeThreadParkerHandle NativeHandle => parker.NativeHandle;

    /// <summary>Cancellation token signaled on shutdown.</summary>
    public CancellationToken CancellationToken => cancellationToken;

    /// <summary>Returns true while cancellation has not been requested.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ShouldContinue() => !cancellationToken.IsCancellationRequested;

    /// <summary>Pre-arms the worker's parker before checking work queues.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryArm() => parker.TryArm();

    /// <summary>Disarms an armed parker if work arrived before parking.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Disarm() => parker.Disarm();

    /// <summary>Parks the worker thread up to <paramref name="timeoutMs"/> milliseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Park(int timeoutMs = -1) => parker.Park(timeoutMs);

    /// <summary>Signals the worker's parker.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unpark() => parker.Unpark();
}
