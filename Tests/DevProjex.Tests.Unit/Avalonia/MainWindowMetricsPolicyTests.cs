using DevProjex.Avalonia.Services;

namespace DevProjex.Tests.Unit.Avalonia;

public sealed class MainWindowMetricsPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ShouldProceedWithMetricsCalculation_ReturnsExpectedDecision(
        bool hasAnyCheckedNodes,
        bool hasCompleteMetricsBaseline,
        bool expected)
    {
        var result = MetricsCalculationPolicy.ShouldProceedWithMetricsCalculation(
            hasAnyCheckedNodes,
            hasCompleteMetricsBaseline);

        Assert.Equal(expected, result);
    }
}
