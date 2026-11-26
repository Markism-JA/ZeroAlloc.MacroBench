using MacroBench.Telemetry;

namespace MacroBench.Execution;

/// <summary>
/// Lightweight adapter used by generated probe registries to apply attribute-configured names,
/// scenario labels, and <see cref="PerformanceBudget"/> overrides without modifying probe classes.
/// </summary>
public sealed class ConfiguredMacroProbe(
    IMacroProbe inner,
    string? nameOverride = null,
    string? scenarioOverride = null,
    PerformanceBudget? budgetOverride = null
) : IMacroProbe
{
    /// <summary>Underlying probe instance.</summary>
    public IMacroProbe Inner => inner;

    /// <inheritdoc />
    public string Name => string.IsNullOrEmpty(nameOverride) ? inner.Name : nameOverride;

    /// <inheritdoc />
    public string Scenario =>
        string.IsNullOrEmpty(scenarioOverride)
            ? inner.Scenario
            : (string.IsNullOrEmpty(inner.Scenario) ? scenarioOverride : $"{inner.Scenario} [{scenarioOverride}]");

    /// <inheritdoc />
    public PerformanceBudget? Budget => budgetOverride ?? inner.Budget;

    /// <inheritdoc />
    public void Setup(ProbeContext context) => inner.Setup(context);

    /// <inheritdoc />
    public void Warmup(ProbeContext context) => inner.Warmup(context);

    /// <inheritdoc />
    public void Measure(ProbeContext context) => inner.Measure(context);

    /// <inheritdoc />
    public void Teardown(ProbeContext context) => inner.Teardown(context);
}
