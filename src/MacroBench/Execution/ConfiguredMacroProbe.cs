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
    public IMacroProbe Inner => inner;
    public string Name => string.IsNullOrEmpty(nameOverride) ? inner.Name : nameOverride;
    public string Scenario =>
        string.IsNullOrEmpty(scenarioOverride)
            ? inner.Scenario
            : (string.IsNullOrEmpty(inner.Scenario) ? scenarioOverride : $"{inner.Scenario} [{scenarioOverride}]");
    public PerformanceBudget? Budget => budgetOverride ?? inner.Budget;

    public void Setup(ProbeContext context) => inner.Setup(context);
    public void Warmup(ProbeContext context) => inner.Warmup(context);
    public void Measure(ProbeContext context) => inner.Measure(context);
    public void Teardown(ProbeContext context) => inner.Teardown(context);
}
