using System.Text.Json;
using MacroBench.Execution;
using MacroBench.Logging;
using MacroBench.Reporting;
using MacroBench.Telemetry;

namespace MacroBench.Tests;

public class ProbeRunnerTests
{
    private sealed class SyntheticProbe(
        string name,
        string scenario,
        PerformanceBudget? budget = null,
        long latencyNs = 500,
        bool allocateOnMeasure = false
    ) : IMacroProbe
    {
        public string Name => name;
        public string Scenario => scenario;
        public PerformanceBudget? Budget => budget;

        public bool SetupCalled { get; private set; }
        public bool WarmupCalled { get; private set; }
        public bool TeardownCalled { get; private set; }

        public void Setup(ProbeContext context) => SetupCalled = true;

        public void Warmup(ProbeContext context)
        {
            WarmupCalled = true;
            // Record noisy warmup samples that must be wiped before Measure
            context.RecordLatencyNs(999_999);
            context.AddOperations(100);
        }

        public void Measure(ProbeContext context)
        {
            for (int i = 0; i < 1000; i++)
            {
                context.RecordLatencyNs(latencyNs);
                context.AddOperations(1);
                context.AddBytes(64);
                if (i % 250 == 0)
                {
                    context.PublishLiveProgress(i + 1, 0, (i + 1) * 64L, 0);
                }
            }

            if (allocateOnMeasure)
            {
                _ = new byte[64 * 1024];
            }

            context.SetCustomMetric("CustomMetric", 42.5);
        }

        public void Teardown(ProbeContext context) => TeardownCalled = true;
    }

    [Fact]
    public void Run_ExecutesLifecycleResetsWarmupAndExportsAllArtifacts()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"macrobench_test_{Guid.NewGuid():N}");
        var consoleLog = new StringWriter();
        var jsonlLog = new StringWriter();
        var consoleTable = new StringWriter();

        try
        {
            var probe = new SyntheticProbe(
                "Synthetic.Ring",
                "64B",
                new PerformanceBudget(MaxP99Ns: 5_000, MaxAllowedDrops: 0)
            );

            var config = ProbeRunConfig.Default
                .WithArtifactsDirectory(tempDir, createTimestampedRunFolder: false)
                .WithDurations(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
                .WithLoggers(
                    new ConsoleProbeLogger(consoleLog, useColor: false),
                    new JsonLinesProbeLogger(jsonlLog)
                )
                .WithExporters(
                    new ConsoleSummaryExporter(consoleTable, useColor: false),
                    Exporters.Json,
                    Exporters.Markdown,
                    Exporters.Csv
                ) with
            {
                ElevateProcessPriority = false,
                UseHighResolutionTimerScope = false,
                CollectGcBeforeMeasure = false,
                LiveProgressInterval = TimeSpan.Zero,
            };

            ProbeRunSummary summary = ProbeRunner.Run(config, [probe]);

            Assert.True(probe.SetupCalled);
            Assert.True(probe.WarmupCalled);
            Assert.True(probe.TeardownCalled);
            Assert.True(summary.AllBudgetsPassed);

            ProbeReport report = Assert.Single(summary.Reports);
            Assert.Equal(1000, report.TotalOperations);
            Assert.Equal(500, report.Latency.MinNs);
            Assert.Equal(500, report.Latency.MaxNs); // Warmup 999_999 sample was reset
            Assert.NotNull(report.CustomMetrics);
            Assert.Equal(42.5, report.CustomMetrics!["CustomMetric"]);

            // Verify all exported artifacts exist and are valid
            string jsonPath = Path.Combine(tempDir, "summary.json");
            string mdPath = Path.Combine(tempDir, "summary.md");
            string csvPath = Path.Combine(tempDir, "summary.csv");
            string pctPath = Path.Combine(tempDir, "percentiles.csv");

            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(mdPath));
            Assert.True(File.Exists(csvPath));
            Assert.True(File.Exists(pctPath));

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            Assert.Equal("Synthetic.Ring", doc.RootElement.GetProperty("Reports")[0].GetProperty("ProbeName").GetString());
            Assert.Contains("Synthetic.Ring", File.ReadAllText(mdPath));
            Assert.Contains("Synthetic.Ring,64B", File.ReadAllText(csvPath));
            Assert.Contains("Synthetic.Ring,64B,99,", File.ReadAllText(pctPath));
            Assert.Contains("[PASS] [Synthetic.Ring]", consoleLog.ToString());
            Assert.Contains("ProbeCompleted", jsonlLog.ToString());
            Assert.Contains("PASS", consoleTable.ToString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Run_WithStrictBudgetEnforcement_ThrowsOnViolation()
    {
        var probe = new SyntheticProbe(
            "Allocating.Probe",
            "BudgetFail",
            new PerformanceBudget(MaxP99Ns: 100, MaxAllowedGcBytes: 0),
            latencyNs: 5_000,
            allocateOnMeasure: true
        );

        var config = ProbeRunConfig.Default
            .WithArtifactsDirectory(null)
            .WithLoggers()
            .WithExporters()
            .WithBudgetEnforcement(true) with
        {
            ElevateProcessPriority = false,
            UseHighResolutionTimerScope = false,
            CollectGcBeforeMeasure = false,
            LiveProgressInterval = TimeSpan.Zero,
        };

        var ex = Assert.Throws<PerformanceBudgetViolationException>(() =>
            ProbeRunner.Run(config, [probe])
        );
        Assert.Contains("Allocating.Probe", ex.Message);
        Assert.True(probe.TeardownCalled);
    }
}
