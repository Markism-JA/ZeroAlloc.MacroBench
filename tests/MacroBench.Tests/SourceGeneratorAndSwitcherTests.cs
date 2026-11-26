using MacroBench.Attributes;
using MacroBench.Execution;
using MacroBench.Generated;
using MacroBench.Reporting;

namespace MacroBench.Tests;

[MacroProbe("Generated.Sweep")]
[ProbeBudget(MaxP99Ns = 50_000, MaxAllowedDrops = 0)]
public sealed class SampleGeneratedSweepProbe : IMacroProbe
{
    [ProbeParams(16, 64)]
    public int BatchSize { get; set; }

    [ProbeParams("Small", "Large")]
    public string Tier { get; set; } = string.Empty;

    public string Name => "IgnoredDefaultName";

    public void Measure(ProbeContext context)
    {
        for (int i = 0; i < BatchSize; i++)
        {
            context.RecordLatencyNs(250);
            context.AddOperations(1);
        }
    }
}

public class SourceGeneratorAndSwitcherTests
{
    [Fact]
    public void GeneratedProbes_ExpandsCartesianParamsAndBudget()
    {
        IReadOnlyList<IMacroProbe> probes = GeneratedProbes.CreateAll();

        var sweepProbes = probes.Where(p => p.Name == "Generated.Sweep").ToList();
        Assert.Equal(4, sweepProbes.Count);

        var scenarios = sweepProbes.Select(p => p.Scenario).ToHashSet();
        Assert.Contains("BatchSize=16, Tier=Small", scenarios);
        Assert.Contains("BatchSize=16, Tier=Large", scenarios);
        Assert.Contains("BatchSize=64, Tier=Small", scenarios);
        Assert.Contains("BatchSize=64, Tier=Large", scenarios);

        Assert.All(sweepProbes, p =>
        {
            Assert.NotNull(p.Budget);
            Assert.Equal(50_000, p.Budget!.Value.MaxP99Ns);
            Assert.Equal(0, p.Budget!.Value.MaxAllowedDrops);
        });
    }

    [Fact]
    public void ProbeSwitcher_ListAndBaselineCompare_WorkEndToEnd()
    {
        string dirA = Path.Combine(Path.GetTempPath(), $"mb_base_{Guid.NewGuid():N}");
        string dirB = Path.Combine(Path.GetTempPath(), $"mb_cur_{Guid.NewGuid():N}");
        var listOut = new StringWriter();
        var runOut = new StringWriter();

        try
        {
            IReadOnlyList<IMacroProbe> probes = GeneratedProbes.CreateAll();

            // 1. --list mode
            int listExit = ProbeSwitcher.Run(["--list", "--filter", "*Generated*"], probes, output: listOut);
            Assert.Equal(0, listExit);
            Assert.Contains("BatchSize=64, Tier=Large", listOut.ToString());

            var fastConfig = ProbeRunConfig.Default
                .WithArtifactsDirectory(dirA, createTimestampedRunFolder: false)
                .WithDurations(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(5)) with
            {
                ElevateProcessPriority = false,
                UseHighResolutionTimerScope = false,
                CollectGcBeforeMeasure = false,
                LiveProgressInterval = TimeSpan.Zero,
            };

            // 2. Run baseline
            int baseExit = ProbeSwitcher.Run(
                ["--filter", "*BatchSize=16, Tier=Small*", "--no-color"],
                probes,
                baseConfig: fastConfig,
                output: runOut
            );
            Assert.Equal(0, baseExit);
            string baselineJson = Path.Combine(dirA, "summary.json");
            Assert.True(File.Exists(baselineJson));

            // 3. Run second pass with --baseline diff
            var secondConfig = fastConfig.WithArtifactsDirectory(dirB, createTimestampedRunFolder: false);
            int curExit = ProbeSwitcher.Run(
                ["--filter", "*BatchSize=16, Tier=Small*", "--baseline", baselineJson, "--no-color"],
                probes,
                baseConfig: secondConfig,
                output: runOut
            );
            Assert.Equal(0, curExit);
            Assert.Contains("Baseline Comparison:", runOut.ToString());
            Assert.Contains("Generated.Sweep", runOut.ToString());
        }
        finally
        {
            if (Directory.Exists(dirA)) Directory.Delete(dirA, recursive: true);
            if (Directory.Exists(dirB)) Directory.Delete(dirB, recursive: true);
        }
    }
}
