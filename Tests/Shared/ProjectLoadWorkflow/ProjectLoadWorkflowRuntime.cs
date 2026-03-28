using DevProjex.Application.Services;
using DevProjex.Application.UseCases;
using DevProjex.Infrastructure.FileSystem;
using DevProjex.Infrastructure.SmartIgnore;
using DevProjex.Kernel.Abstractions;
using DevProjex.Kernel.Contracts;

namespace DevProjex.Tests.Shared.ProjectLoadWorkflow;

public static class ProjectLoadWorkflowRuntime
{
    public static LocalizationService CreateLocalizationService() =>
        new(new WorkflowLocalizationCatalog(), AppLanguage.En);

    public static IgnoreOptionsService CreateIgnoreOptionsService() =>
        new(CreateLocalizationService());

    public static IgnoreRulesService CreateIgnoreRulesService()
    {
        var smartIgnoreRules = new ISmartIgnoreRule[]
        {
            new CommonSmartIgnoreRule(),
            new FrontendArtifactsIgnoreRule(),
            new DotNetArtifactsIgnoreRule(),
            new PythonArtifactsIgnoreRule(),
            new JvmArtifactsIgnoreRule(),
            new RustArtifactsIgnoreRule(),
            new GoArtifactsIgnoreRule(),
            new PhpArtifactsIgnoreRule(),
            new RubyArtifactsIgnoreRule()
        };

        return new IgnoreRulesService(new SmartIgnoreService(smartIgnoreRules));
    }

    public static BuildTreeUseCase CreateBuildTreeUseCase() =>
        new(
            new TreeBuilder(),
            new TreeNodePresentationService(CreateLocalizationService(), new WorkflowIconMapper()));

    public static async Task<ProjectLoadWorkflowMetrics> ComputeMetricsAsync(
        string rootPath,
        IReadOnlyCollection<string> selectedRoots,
        IReadOnlyCollection<string> allowedExtensions,
        IReadOnlyCollection<IgnoreOptionId> selectedIgnoreOptions,
        CancellationToken cancellationToken)
    {
        // The runtime UI applies selections using set semantics, not list ordering.
        // The test harness must mirror that behavior exactly or the "expected" metrics
        // drift away from the real BuildTree/Export pipeline on large workspaces.
        var selectedRootSet = new HashSet<string>(selectedRoots, PathComparer.Default);
        var allowedExtensionSet = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
        var ignoreRulesService = CreateIgnoreRulesService();
        var ignoreRules = ignoreRulesService.Build(rootPath, selectedIgnoreOptions, selectedRootSet);

        var buildTreeUseCase = CreateBuildTreeUseCase();
        var buildResult = buildTreeUseCase.Execute(new BuildTreeRequest(
            rootPath,
            new TreeFilterOptions(
                AllowedExtensions: allowedExtensionSet,
                AllowedRootFolders: selectedRootSet,
                IgnoreRules: ignoreRules)));

        var treeExport = new TreeExportService();
        var treeText = treeExport.BuildFullTree(rootPath, buildResult.Root, TreeTextFormat.Ascii);
        var treeMetrics = ExportOutputMetricsCalculator.FromText(treeText);

        var contentExport = new SelectedContentExportService(new FileContentAnalyzer());
        var contentText = await contentExport.BuildAsync(
            EnumerateAllFiles(buildResult.Root),
            cancellationToken);
        var contentMetrics = ExportOutputMetricsCalculator.FromText(contentText);

        return new ProjectLoadWorkflowMetrics(treeMetrics, contentMetrics);
    }

    public static IEnumerable<string> EnumerateAllFiles(TreeNodeDescriptor node)
    {
        // An iterative walk avoids hiding recursion-related issues behind a helper that
        // silently fails only on deeper trees than the tiny unit fixtures usually cover.
        var stack = new Stack<TreeNodeDescriptor>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!current.IsDirectory)
            {
                yield return current.FullPath;
                continue;
            }

            for (var index = current.Children.Count - 1; index >= 0; index--)
                stack.Push(current.Children[index]);
        }
    }

    public sealed record ProjectLoadWorkflowMetrics(
        ExportOutputMetrics TreeMetrics,
        ExportOutputMetrics ContentMetrics);

    private sealed class WorkflowLocalizationCatalog : ILocalizationCatalog
    {
        public IReadOnlyDictionary<string, string> Get(AppLanguage language)
        {
            return new Dictionary<string, string>
            {
                ["Tree.AccessDeniedRoot"] = "Access denied",
                ["Tree.AccessDenied"] = "Access denied",
                ["Settings.Ignore.SmartIgnore"] = "Smart ignore",
                ["Settings.Ignore.UseGitIgnore"] = "Use .gitignore",
                ["Settings.Ignore.HiddenFolders"] = "Hidden folders",
                ["Settings.Ignore.HiddenFiles"] = "Hidden files",
                ["Settings.Ignore.DotFolders"] = "Dot folders",
                ["Settings.Ignore.DotFiles"] = "Dot files",
                ["Settings.Ignore.EmptyFolders"] = "Empty folders",
                ["Settings.Ignore.EmptyFiles"] = "Empty files",
                ["Settings.Ignore.ExtensionlessFiles"] = "Files without extension"
            };
        }
    }

    private sealed class WorkflowIconMapper : IIconMapper
    {
        public string GetIconKey(FileSystemNode node) => node.IsDirectory ? "folder" : "file";
    }
}
