namespace DevProjex.Tests.Integration.Performance;

public sealed class PerfBaselineStoreTests
{
    [Fact]
    public void Evaluate_WhenBaselineMissing_CreatesBaselineAndReturnsNonRegression()
    {
        using var temp = new TemporaryDirectory();
        var baselinePath = Path.Combine(temp.Path, "perf-baseline.json");
        var store = new PerfBaselineStore(baselinePath);
        var measurement = new PerfMeasurement(
            MedianMilliseconds: 100,
            P95Milliseconds: 110,
            MedianAllocatedBytes: 100_000,
            MaxAllocatedBytes: 120_000,
            Iterations: 3);

        var verdict = store.Evaluate("scenario-a", measurement);

        Assert.False(verdict.IsRegression);
        Assert.False(verdict.IsAcceleration);
        Assert.Contains("[baseline-created]", verdict.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(baselinePath));

        using var doc = JsonDocument.Parse(File.ReadAllText(baselinePath));
        Assert.True(doc.RootElement.TryGetProperty("Scenarios", out var scenarios));
        Assert.True(scenarios.TryGetProperty("scenario-a", out _));
    }

    [Fact]
    public void Evaluate_WhenBaselineIsCorrupted_RecreatesSnapshotAndContinues()
    {
        using var temp = new TemporaryDirectory();
        var baselinePath = Path.Combine(temp.Path, "perf-baseline.json");
        File.WriteAllText(baselinePath, "{ this is invalid json");

        var store = new PerfBaselineStore(baselinePath);
        var measurement = new PerfMeasurement(
            MedianMilliseconds: 90,
            P95Milliseconds: 100,
            MedianAllocatedBytes: 80_000,
            MaxAllocatedBytes: 95_000,
            Iterations: 3);

        var verdict = store.Evaluate("scenario-corrupted", measurement);

        Assert.False(verdict.IsRegression);
        Assert.Contains("[baseline-created]", verdict.Message, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(baselinePath));
        Assert.True(doc.RootElement.TryGetProperty("Scenarios", out var scenarios));
        Assert.True(scenarios.TryGetProperty("scenario-corrupted", out _));
    }

    [Fact]
    public void Evaluate_WhenBaselineUpdateEnabled_OverwritesStoredBaselineValues()
    {
        using var temp = new TemporaryDirectory();
        var baselinePath = Path.Combine(temp.Path, "perf-baseline.json");
        var store = new PerfBaselineStore(baselinePath);

        var initial = new PerfMeasurement(
            MedianMilliseconds: 100,
            P95Milliseconds: 120,
            MedianAllocatedBytes: 100_000,
            MaxAllocatedBytes: 130_000,
            Iterations: 3);
        var updated = new PerfMeasurement(
            MedianMilliseconds: 160,
            P95Milliseconds: 180,
            MedianAllocatedBytes: 210_000,
            MaxAllocatedBytes: 260_000,
            Iterations: 3);

        store.Evaluate("scenario-update", initial);
        using (EnvVarScope.Set(LocalPerformanceSettings.UpdateBaselineEnvVar, "1"))
        {
            store.Evaluate("scenario-update", updated);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var scenario = doc.RootElement
            .GetProperty("Scenarios")
            .GetProperty("scenario-update");

        Assert.Equal(160, scenario.GetProperty("MedianMilliseconds").GetDouble());
        Assert.Equal(210_000, scenario.GetProperty("MedianAllocatedBytes").GetInt64());
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        private EnvVarScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static EnvVarScope Set(string name, string? value) => new(name, value);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}
