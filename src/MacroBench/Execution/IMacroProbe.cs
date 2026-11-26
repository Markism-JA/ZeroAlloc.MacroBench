using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Contract for a macro-benchmark probe separated cleanly from runner orchestration, logging, and artifact export.
/// </summary>
public interface IMacroProbe
{
    string Name { get; }
    string Scenario => string.Empty;
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
