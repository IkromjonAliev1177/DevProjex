using DevProjex.Tests.Integration.Helpers;
using DevProjex.Tests.Shared.ProjectLoadWorkflow;
using static DevProjex.Tests.Shared.ProjectLoadWorkflow.ProjectLoadWorkflowRefreshHarness;

namespace DevProjex.Tests.Integration;

public sealed class ProjectLoadWorkflowCrossSectionStateMatrixIntegrationTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ComputeFullRefreshSnapshot_CrossSectionStateMatrix_ConvergesAndPreservesSelectionContracts(
        string rootScenarioName,
        string extensionScenarioName,
        string ignoreScenarioName)
    {
        var rootScenario = Enum.Parse<WorkflowRootScenario>(rootScenarioName);
        var extensionScenario = Enum.Parse<WorkflowExtensionScenario>(extensionScenarioName);
        var ignoreScenario = Enum.Parse<WorkflowIgnoreScenario>(ignoreScenarioName);

        using var temp = new TemporaryDirectory();
        ProjectLoadWorkflowWorkspaceSeeder.Seed(temp.Path);

        var services = CreateServices();
        var baselineSnapshot = services.Engine.ComputeFullRefreshSnapshot(
            CreateDefaultContext(temp.Path),
            CancellationToken.None);
        var scenario = CreateScenario(baselineSnapshot, rootScenario, extensionScenario, ignoreScenario);

        var firstSnapshot = services.Engine.ComputeFullRefreshSnapshot(
            CreateScenarioContext(temp.Path, scenario),
            CancellationToken.None);

        AssertVisibleAdvancedIgnoreOptionsCarryPositiveCounts(firstSnapshot);
        AssertScenarioSelectionContract(firstSnapshot, scenario);

        var secondSnapshot = services.Engine.ComputeFullRefreshSnapshot(
            BuildConvergedContext(temp.Path, firstSnapshot),
            CancellationToken.None);

        AssertEquivalentSnapshots(firstSnapshot, secondSnapshot);
        AssertVisibleAdvancedIgnoreOptionsCarryPositiveCounts(secondSnapshot);
        AssertScenarioSelectionContract(secondSnapshot, scenario);

        var firstMetrics = await ComputeMetricsFromSnapshotAsync(temp.Path, firstSnapshot);
        var secondMetrics = await ComputeMetricsFromSnapshotAsync(temp.Path, secondSnapshot);
        Assert.Equal(firstMetrics.TreeMetrics, secondMetrics.TreeMetrics);
        Assert.Equal(firstMetrics.ContentMetrics, secondMetrics.ContentMetrics);
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var rootScenario in Enum.GetValues<WorkflowRootScenario>())
        {
            foreach (var extensionScenario in Enum.GetValues<WorkflowExtensionScenario>())
            {
                foreach (var ignoreScenario in Enum.GetValues<WorkflowIgnoreScenario>())
                    yield return [rootScenario.ToString(), extensionScenario.ToString(), ignoreScenario.ToString()];
            }
        }
    }
}
