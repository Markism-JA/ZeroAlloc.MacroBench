using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MacroBench.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class MacroProbeGenerator : IIncrementalGenerator
{
    private const string MacroProbeAttr = "MacroBench.Attributes.MacroProbeAttribute";
    private const string ProbeParamsAttr = "MacroBench.Attributes.ProbeParamsAttribute";
    private const string ProbeBudgetAttr = "MacroBench.Attributes.ProbeBudgetAttribute";
    private const string MacroProbeInterface = "MacroBench.Execution.IMacroProbe";

    private static readonly DiagnosticDescriptor InvalidProbeDiagnostic = new(
        id: "MB001",
        title: "Invalid MacroProbe declaration",
        messageFormat: "Type '{0}' annotated with [MacroProbe] must be non-abstract, implement IMacroProbe, and have an accessible parameterless constructor",
        category: "MacroBench",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ProbeModel?> candidates = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: MacroProbeAttr,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => ExtractProbeModel(ctx)
            );

        IncrementalValueProvider<ImmutableArray<ProbeModel?>> collected = candidates.Collect();

        context.RegisterSourceOutput(
            collected,
            static (spc, models) => EmitRegistry(spc, models)
        );
    }

    private static ProbeModel? ExtractProbeModel(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        bool implementsInterface = symbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == MacroProbeInterface
        );
        bool hasParameterlessCtor = symbol.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0
            && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
        );

        if (symbol.IsAbstract || !implementsInterface || !hasParameterlessCtor)
        {
            return new ProbeModel(
                FullyQualifiedName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                NameOverride: null,
                Parameters: ImmutableArray<ParamMember>.Empty,
                Budget: null,
                InvalidLocation: ctx.TargetNode.GetLocation(),
                InvalidTypeName: symbol.Name
            );
        }

        string? nameOverride = null;
        AttributeData? probeAttr = ctx.Attributes.FirstOrDefault();
        if (probeAttr is not null && probeAttr.ConstructorArguments.Length == 1)
        {
            nameOverride = probeAttr.ConstructorArguments[0].Value as string;
        }

        // Extract [ProbeParams] members
        var paramBuilder = ImmutableArray.CreateBuilder<ParamMember>();
        foreach (ISymbol member in symbol.GetMembers())
        {
            AttributeData? paramAttr = member
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ProbeParamsAttr);
            if (paramAttr is null || paramAttr.ConstructorArguments.Length != 1)
                continue;

            TypedConstant arg = paramAttr.ConstructorArguments[0];
            if (arg.Kind != TypedConstantKind.Array || arg.Values.IsDefaultOrEmpty)
                continue;

            ITypeSymbol? memberType = member switch
            {
                IPropertySymbol prop when prop.SetMethod is not null => prop.Type,
                IFieldSymbol field when !field.IsReadOnly && !field.IsConst => field.Type,
                _ => null,
            };

            if (memberType is null)
                continue;

            var literals = ImmutableArray.CreateBuilder<ParamValue>(arg.Values.Length);
            foreach (TypedConstant val in arg.Values)
            {
                literals.Add(new ParamValue(FormatLiteral(val, memberType), FormatDisplay(val)));
            }

            paramBuilder.Add(new ParamMember(member.Name, literals.ToImmutable()));
        }

        // Extract optional [ProbeBudget]
        BudgetModel? budget = null;
        AttributeData? budgetAttr = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ProbeBudgetAttr);
        if (budgetAttr is not null)
        {
            long maxP50 = long.MaxValue;
            long maxP99 = long.MaxValue;
            long maxP999 = long.MaxValue;
            long maxDrops = long.MaxValue;
            long maxGc = long.MaxValue;
            double minMpps = 0.0;
            int maxGen0 = int.MaxValue;

            foreach (KeyValuePair<string, TypedConstant> kvp in budgetAttr.NamedArguments)
            {
                switch (kvp.Key)
                {
                    case "MaxP50Ns": maxP50 = Convert.ToInt64(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                    case "MaxP99Ns": maxP99 = Convert.ToInt64(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                    case "MaxP999Ns": maxP999 = Convert.ToInt64(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                    case "MaxAllowedDrops": maxDrops = Convert.ToInt64(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                    case "MaxAllowedGcBytes": maxGc = Convert.ToInt64(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                    case "MinThroughputMpps": minMpps = Convert.ToDouble(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                    case "MaxGen0Collections": maxGen0 = Convert.ToInt32(kvp.Value.Value, CultureInfo.InvariantCulture); break;
                }
            }

            budget = new BudgetModel(maxP50, maxP99, maxP999, maxDrops, maxGc, minMpps, maxGen0);
        }

        return new ProbeModel(
            FullyQualifiedName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            NameOverride: nameOverride,
            Parameters: paramBuilder.ToImmutable(),
            Budget: budget,
            InvalidLocation: null,
            InvalidTypeName: null
        );
    }

    private static void EmitRegistry(
        SourceProductionContext spc,
        ImmutableArray<ProbeModel?> rawModels
    )
    {
        var validModels = new List<ProbeModel>();
        foreach (ProbeModel? m in rawModels)
        {
            if (m is null)
                continue;

            if (m.InvalidLocation is not null)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(InvalidProbeDiagnostic, m.InvalidLocation, m.InvalidTypeName)
                );
                continue;
            }

            validModels.Add(m);
        }

        if (validModels.Count == 0)
            return;

        validModels.Sort((a, b) => StringComparer.Ordinal.Compare(a.FullyQualifiedName, b.FullyQualifiedName));

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace MacroBench.Generated;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Compile-time generated registry of all [MacroProbe] classes and parameter sweeps in this assembly.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class GeneratedProbes");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::MacroBench.Execution.IMacroProbe> CreateAll()");
        sb.AppendLine("    {");
        sb.AppendLine("        return new global::MacroBench.Execution.IMacroProbe[]");
        sb.AppendLine("        {");

        foreach (ProbeModel model in validModels)
        {
            List<List<(string MemberName, ParamValue Value)>> combinations = ExpandCartesian(model.Parameters);

            foreach (List<(string MemberName, ParamValue Value)> combo in combinations)
            {
                string ctorExpr;
                if (combo.Count == 0)
                {
                    ctorExpr = $"new {model.FullyQualifiedName}()";
                }
                else
                {
                    string initList = string.Join(
                        ", ",
                        combo.Select(c => $"{c.MemberName} = {c.Value.Literal}")
                    );
                    ctorExpr = $"new {model.FullyQualifiedName} {{ {initList} }}";
                }

                bool needsAdapter =
                    !string.IsNullOrEmpty(model.NameOverride)
                    || combo.Count > 0
                    || model.Budget is not null;

                if (!needsAdapter)
                {
                    sb.AppendLine($"            {ctorExpr},");
                }
                else
                {
                    string nameArg = model.NameOverride is null
                        ? "null"
                        : SymbolDisplay.FormatLiteral(model.NameOverride, quote: true);
                    string scenarioArg = combo.Count == 0
                        ? "null"
                        : SymbolDisplay.FormatLiteral(
                            string.Join(", ", combo.Select(c => $"{c.MemberName}={c.Value.Display}")),
                            quote: true
                        );
                    string budgetArg = model.Budget is null
                        ? "null"
                        : FormatBudget(model.Budget);

                    sb.AppendLine(
                        $"            new global::MacroBench.Execution.ConfiguredMacroProbe({ctorExpr}, {nameArg}, {scenarioArg}, {budgetArg}),"
                    );
                }
            }
        }

        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("GeneratedProbes.g.cs", sb.ToString());
    }

    private static List<List<(string MemberName, ParamValue Value)>> ExpandCartesian(
        ImmutableArray<ParamMember> members
    )
    {
        var result = new List<List<(string, ParamValue)>> { new() };
        foreach (ParamMember member in members)
        {
            var next = new List<List<(string, ParamValue)>>();
            foreach (List<(string, ParamValue)> prefix in result)
            {
                foreach (ParamValue val in member.Values)
                {
                    var copy = new List<(string, ParamValue)>(prefix) { (member.Name, val) };
                    next.Add(copy);
                }
            }
            result = next;
        }
        return result;
    }

    private static string FormatBudget(BudgetModel b) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "new global::MacroBench.Telemetry.PerformanceBudget(MaxP50Ns: {0}L, MaxP99Ns: {1}L, MaxP999Ns: {2}L, MaxAllowedDrops: {3}L, MaxAllowedGcBytes: {4}L, MinThroughputMpps: {5:R}, MaxGen0Collections: {6})",
            b.MaxP50Ns,
            b.MaxP99Ns,
            b.MaxP999Ns,
            b.MaxAllowedDrops,
            b.MaxAllowedGcBytes,
            b.MinThroughputMpps,
            b.MaxGen0Collections
        );

    private static string FormatLiteral(TypedConstant val, ITypeSymbol targetType)
    {
        if (val.IsNull)
            return "null!";

        if (targetType.TypeKind == TypeKind.Enum)
        {
            string typeName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({typeName}){Convert.ToInt64(val.Value, CultureInfo.InvariantCulture)}";
        }

        return val.Value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            bool b => b ? "true" : "false",
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
            uint ui => ui.ToString(CultureInfo.InvariantCulture) + "U",
            int i => i.ToString(CultureInfo.InvariantCulture),
            short sh => $"(short){sh.ToString(CultureInfo.InvariantCulture)}",
            ushort ush => $"(ushort){ush.ToString(CultureInfo.InvariantCulture)}",
            byte by => $"(byte){by.ToString(CultureInfo.InvariantCulture)}",
            sbyte sby => $"(sbyte){sby.ToString(CultureInfo.InvariantCulture)}",
            _ => Convert.ToString(val.Value, CultureInfo.InvariantCulture) ?? "default",
        };
    }

    private static string FormatDisplay(TypedConstant val) =>
        val.IsNull ? "null" : Convert.ToString(val.Value, CultureInfo.InvariantCulture) ?? "";

    private sealed record ParamValue(string Literal, string Display);
    private sealed record ParamMember(string Name, ImmutableArray<ParamValue> Values);
    private sealed record BudgetModel(
        long MaxP50Ns,
        long MaxP99Ns,
        long MaxP999Ns,
        long MaxAllowedDrops,
        long MaxAllowedGcBytes,
        double MinThroughputMpps,
        int MaxGen0Collections
    );
    private sealed record ProbeModel(
        string FullyQualifiedName,
        string? NameOverride,
        ImmutableArray<ParamMember> Parameters,
        BudgetModel? Budget,
        Location? InvalidLocation,
        string? InvalidTypeName
    );
}
