using DevProjex.Avalonia.Services;

namespace DevProjex.Tests.Unit.Avalonia;

public sealed class MainWindowPreviewWarmupTests
{
    [Fact]
    public void CountSelectedFilesUpToLimit_IgnoresMissingFilesAndStopsAtLimit()
    {
        using var temp = new TemporaryDirectory();
        var first = temp.CreateFile("a.txt", "a");
        var second = temp.CreateFile("b.txt", "b");
        var third = temp.CreateFile("c.txt", "c");
        var missing = Path.Combine(temp.Path, "missing.txt");

        var result = PreviewWarmupPolicy.CountSelectedFilesUpToLimit(
            new HashSet<string>(PathComparer.Default) { missing, third, first, second },
            2);

        Assert.Equal(2, result);
    }

    [Fact]
    public void CountTreeFilesUpToLimit_CountsLeafFilesOnly()
    {
        var treeRoot = new TreeNodeDescriptor(
            DisplayName: "root",
            FullPath: CreatePath("root"),
            IsDirectory: true,
            IsAccessDenied: false,
            IconKey: "folder",
            Children:
            [
                new TreeNodeDescriptor(
                    DisplayName: "src",
                    FullPath: CreatePath("root", "src"),
                    IsDirectory: true,
                    IsAccessDenied: false,
                    IconKey: "folder",
                    Children:
                    [
                        CreateFileDescriptor("one.cs"),
                        CreateFileDescriptor("two.cs")
                    ]),
                CreateFileDescriptor("readme.md")
            ]);

        var result = PreviewWarmupPolicy.CountTreeFilesUpToLimit(treeRoot, 2);

        Assert.Equal(2, result);
    }

    [Fact]
    public void CollectInitialPreviewFiles_FromSelection_DedupesSortsAndFiltersMissing()
    {
        using var temp = new TemporaryDirectory();
        var alpha = temp.CreateFile("alpha.txt", "alpha");
        var beta = temp.CreateFile("beta.txt", "beta");
        var missing = Path.Combine(temp.Path, "missing.txt");

        var files = PreviewWarmupPolicy.CollectInitialPreviewFiles(
            new HashSet<string>(PathComparer.Default) { beta, missing, alpha, beta },
            true,
            null,
            10);

        Assert.Equal(
            new[] { alpha, beta }.OrderBy(static path => path, PathComparer.Default),
            files);
    }

    [Fact]
    public void CollectInitialPreviewFiles_FromTree_ReturnsOrderedUniqueFilesUpToLimit()
    {
        using var temp = new TemporaryDirectory();
        var zeta = temp.CreateFile("zeta.txt", "z");
        var alpha = temp.CreateFile("alpha.txt", "a");
        var beta = temp.CreateFile("beta.txt", "b");
        var missing = Path.Combine(temp.Path, "missing.txt");

        var treeRoot = new TreeNodeDescriptor(
            DisplayName: "root",
            FullPath: temp.Path,
            IsDirectory: true,
            IsAccessDenied: false,
            IconKey: "folder",
            Children:
            [
                new TreeNodeDescriptor("group", Path.Combine(temp.Path, "group"), true, false, "folder",
                [
                    new TreeNodeDescriptor("alpha.txt", alpha, false, false, "file", []),
                    new TreeNodeDescriptor("missing.txt", missing, false, false, "file", []),
                    new TreeNodeDescriptor("beta.txt", beta, false, false, "file", [])
                ]),
                new TreeNodeDescriptor("zeta.txt", zeta, false, false, "file", [])
            ]);

        var files = PreviewWarmupPolicy.CollectInitialPreviewFiles(
            new HashSet<string>(PathComparer.Default),
            false,
            treeRoot,
            2);

        Assert.Equal(
            new[] { alpha, beta }.OrderBy(static path => path, PathComparer.Default),
            files);
    }

    [Fact]
    public void ShouldBuildPreviewWarmup_TreeModeAlwaysReturnsFalse()
    {
        using var temp = new TemporaryDirectory();
        var selectedPaths = Enumerable.Range(0, 150)
            .Select(index => temp.CreateFile($"file{index:000}.txt", "x"))
            .ToHashSet(PathComparer.Default);

        var result = PreviewWarmupPolicy.ShouldBuildPreviewWarmup(
            PreviewContentMode.Tree,
            true,
            selectedPaths,
            null);

        Assert.False(result);
    }

    [Fact]
    public void ShouldBuildPreviewWarmup_ContentModeRequiresSelectionThreshold()
    {
        using var temp = new TemporaryDirectory();
        var selectedPaths = Enumerable.Range(0, 140)
            .Select(index => temp.CreateFile($"file{index:000}.txt", "x"))
            .ToHashSet(PathComparer.Default);

        var result = PreviewWarmupPolicy.ShouldBuildPreviewWarmup(
            PreviewContentMode.Content,
            true,
            selectedPaths,
            null);

        Assert.True(result);
    }

    private static TreeNodeDescriptor CreateFileDescriptor(string name)
    {
        var path = CreatePath("root", name);
        return new TreeNodeDescriptor(name, path, false, false, "file", []);
    }

    private static string CreatePath(params string[] segments)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(["C:\\", ..segments])
            : Path.Combine(["/", ..segments]);
    }
}
