using DevProjex.Tests.Integration.Helpers;
using DevProjex.Tests.Shared.ProjectLoadWorkflow;
using static DevProjex.Tests.Shared.ProjectLoadWorkflow.ProjectLoadWorkflowRefreshHarness;

namespace DevProjex.Tests.Integration;

public sealed class ProjectLoadWorkflowMutationOrderMatrixIntegrationTests
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ComputeFullRefreshSnapshot_FinalStateIsIndependentFromSectionMutationOrder(
        string rootScenarioName,
        string extensionScenarioName,
        string ignoreScenarioName,
        string[] mutationOrderNames)
    {
        var rootScenario = Enum.Parse<WorkflowRootScenario>(rootScenarioName);
        var extensionScenario = Enum.Parse<WorkflowExtensionScenario>(extensionScenarioName);
        var ignoreScenario = Enum.Parse<WorkflowIgnoreScenario>(ignoreScenarioName);
        var mutationOrder = mutationOrderNames.Select(name => Enum.Parse<WorkflowMutationStep>(name)).ToArray();

        using var temp = new TemporaryDirectory();
        ProjectLoadWorkflowWorkspaceSeeder.Seed(temp.Path);

        var services = CreateServices();
        var baselineSnapshot = services.Engine.ComputeFullRefreshSnapshot(
            CreateDefaultContext(temp.Path),
            CancellationToken.None);
        var targetScenario = CreateScenario(baselineSnapshot, rootScenario, extensionScenario, ignoreScenario);

        var directSnapshot = services.Engine.ComputeFullRefreshSnapshot(
            CreateScenarioContext(temp.Path, targetScenario),
            CancellationToken.None);
        var directConverged = services.Engine.ComputeFullRefreshSnapshot(
            BuildConvergedContext(temp.Path, directSnapshot),
            CancellationToken.None);

        AssertEquivalentSnapshots(directSnapshot, directConverged);
        AssertScenarioSelectionContract(directConverged, targetScenario);

        var currentSnapshot = baselineSnapshot;
        foreach (var step in mutationOrder)
        {
            var stepContext = ApplyScenarioStep(temp.Path, currentSnapshot, targetScenario, step);
            currentSnapshot = services.Engine.ComputeFullRefreshSnapshot(stepContext, CancellationToken.None);
        }

        var orderedConverged = services.Engine.ComputeFullRefreshSnapshot(
            BuildConvergedContext(temp.Path, currentSnapshot),
            CancellationToken.None);

        AssertEquivalentVisibleSnapshots(directConverged, orderedConverged);
        AssertVisibleAdvancedIgnoreOptionsCarryPositiveCounts(orderedConverged);
        AssertScenarioSelectionContract(orderedConverged, targetScenario);

        var directMetrics = await ComputeMetricsFromSnapshotAsync(temp.Path, directConverged);
        var orderedMetrics = await ComputeMetricsFromSnapshotAsync(temp.Path, orderedConverged);
        Assert.Equal(directMetrics.TreeMetrics, orderedMetrics.TreeMetrics);
        Assert.Equal(directMetrics.ContentMetrics, orderedMetrics.ContentMetrics);
    }

    public static IEnumerable<object[]> Cases()
    {
        var targetStates = new[]
        {
            (WorkflowRootScenario.AllVisible, WorkflowExtensionScenario.AllVisible, WorkflowIgnoreScenario.Defaults),
            (WorkflowRootScenario.DocsOnly, WorkflowExtensionScenario.DocsOnly, WorkflowIgnoreScenario.Defaults),
            (WorkflowRootScenario.SrcOnly, WorkflowExtensionScenario.CodeOnly, WorkflowIgnoreScenario.Defaults),
            (WorkflowRootScenario.SamplesOnly, WorkflowExtensionScenario.CodeAndDocs, WorkflowIgnoreScenario.AllOff),
            (WorkflowRootScenario.DocsAndSrc, WorkflowExtensionScenario.CodeAndDocs, WorkflowIgnoreScenario.GitIgnoreOff),
            (WorkflowRootScenario.DocsAndSamples, WorkflowExtensionScenario.MarkdownOnly, WorkflowIgnoreScenario.EmptyFoldersOff),
            (WorkflowRootScenario.AllVisible, WorkflowExtensionScenario.CodeOnly, WorkflowIgnoreScenario.FileDynamicsOff),
            (WorkflowRootScenario.AllVisible, WorkflowExtensionScenario.DocsOnly, WorkflowIgnoreScenario.DirectoryDynamicsOff),
            (WorkflowRootScenario.DocsOnly, WorkflowExtensionScenario.JsonOnly, WorkflowIgnoreScenario.MixedManual),
            (WorkflowRootScenario.SrcOnly, WorkflowExtensionScenario.AllVisible, WorkflowIgnoreScenario.MixedManual),
            (WorkflowRootScenario.DocsAndSrc, WorkflowExtensionScenario.MarkdownOnly, WorkflowIgnoreScenario.AllOff),
            (WorkflowRootScenario.DocsAndSamples, WorkflowExtensionScenario.CodeOnly, WorkflowIgnoreScenario.GitIgnoreOff)
        };

        var orders = new[]
        {
            new[] { WorkflowMutationStep.Roots, WorkflowMutationStep.Extensions, WorkflowMutationStep.Ignore },
            new[] { WorkflowMutationStep.Roots, WorkflowMutationStep.Ignore, WorkflowMutationStep.Extensions },
            new[] { WorkflowMutationStep.Extensions, WorkflowMutationStep.Roots, WorkflowMutationStep.Ignore },
            new[] { WorkflowMutationStep.Extensions, WorkflowMutationStep.Ignore, WorkflowMutationStep.Roots },
            new[] { WorkflowMutationStep.Ignore, WorkflowMutationStep.Roots, WorkflowMutationStep.Extensions },
            new[] { WorkflowMutationStep.Ignore, WorkflowMutationStep.Extensions, WorkflowMutationStep.Roots }
        };

        foreach (var state in targetStates)
        {
            foreach (var order in orders)
                yield return [
                    state.Item1.ToString(),
                    state.Item2.ToString(),
                    state.Item3.ToString(),
                    order.Select(step => step.ToString()).ToArray()
                ];
        }
    }
}
