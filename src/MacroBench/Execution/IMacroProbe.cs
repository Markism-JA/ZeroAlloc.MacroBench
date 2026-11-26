using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Contract for a synchronous macro-benchmark probe separated cleanly from runner orchestration, logging, and artifact export.
/// </summary>
public interface IMacroProbe
{
    /// <summary>Primary display name of the probe.</summary>
    string Name { get; }

    /// <summary>Optional scenario or parameter label.</summary>
    string Scenario => string.Empty;

    /// <summary>Optional performance budget evaluated after measurement.</summary>
    PerformanceBudget? Budget => null;

    /// <summary>Allocates resources, spawns daemons, and wires transport/pipelines prior to warmup.</summary>
    void Setup(ProbeContext context) { }

    /// <summary>Primes JIT, caches, and worker threads before measurement begins. Counters are reset automatically afterward.</summary>
    void Warmup(ProbeContext context) { }

    /// <summary>Executes the timed measurement workload, recording latencies and counters into <paramref name="context"/>.</summary>
    void Measure(ProbeContext context);

    /// <summary>Disposes workers, sockets, and unmanaged resources allocated in <see cref="Setup"/>.</summary>
    void Teardown(ProbeContext context) { }
}

/// <summary>
/// Asynchronous macro-benchmark probe contract for probes involving async I/O (e.g., NATS, PostgreSQL, sockets).
/// Awaited natively by <see cref="ProbeRunner.RunAsync(ProbeRunConfig, IEnumerable{IMacroProbe}, CancellationToken)"/>
/// and discovered automatically by <c>[MacroProbe]</c>.
/// </summary>
public interface IAsyncMacroProbe : IMacroProbe
{
    /// <summary>Asynchronously allocates resources and connects external clients prior to warmup.</summary>
    ValueTask SetupAsync(ProbeContext context) => ValueTask.CompletedTask;

    /// <summary>Asynchronously warms up connections, pools, and pipelines prior to measurement.</summary>
    ValueTask WarmupAsync(ProbeContext context) => ValueTask.CompletedTask;

    /// <summary>Asynchronously executes the timed measurement workload.</summary>
    ValueTask MeasureAsync(ProbeContext context);

    /// <summary>Asynchronously disconnects and disposes resources allocated in <see cref="SetupAsync"/>.</summary>
    ValueTask TeardownAsync(ProbeContext context) => ValueTask.CompletedTask;

    void IMacroProbe.Setup(ProbeContext context) => SetupAsync(context).AsTask().GetAwaiter().GetResult();
    void IMacroProbe.Warmup(ProbeContext context) => WarmupAsync(context).AsTask().GetAwaiter().GetResult();
    void IMacroProbe.Measure(ProbeContext context) => MeasureAsync(context).AsTask().GetAwaiter().GetResult();
    void IMacroProbe.Teardown(ProbeContext context) => TeardownAsync(context).AsTask().GetAwaiter().GetResult();
}
