namespace MacroBench.Attributes;

/// <summary>
/// Marks an <see cref="Execution.IMacroProbe"/> class for compile-time discovery by the
/// <c>ZeroAlloc.MacroBench</c> Roslyn source generator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MacroProbeAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

/// <summary>
/// Declares parameter sweep values on a public settable property or field of a <see cref="MacroProbeAttribute"/> class.
/// Multiple <see cref="ProbeParamsAttribute"/> members expand into a Cartesian scenario matrix at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
public sealed class ProbeParamsAttribute(params object[] values) : Attribute
{
    public object[] Values { get; } = values;
}

/// <summary>
/// Declares a compile-time <see cref="Telemetry.PerformanceBudget"/> contract on a <see cref="MacroProbeAttribute"/> class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ProbeBudgetAttribute : Attribute
{
    public long MaxP50Ns { get; set; } = long.MaxValue;
    public long MaxP99Ns { get; set; } = long.MaxValue;
    public long MaxP999Ns { get; set; } = long.MaxValue;
    public long MaxAllowedDrops { get; set; } = long.MaxValue;
    public long MaxAllowedGcBytes { get; set; } = long.MaxValue;
    public double MinThroughputMpps { get; set; } = 0.0;
    public int MaxGen0Collections { get; set; } = int.MaxValue;
}
