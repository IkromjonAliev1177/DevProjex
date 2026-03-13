using DevProjex.Avalonia.Services;

namespace DevProjex.Tests.Unit.Avalonia;

public sealed class NameFilterMatchCounterTests
{
    [Fact]
    public void CountMatchesUnderRoot_CountsOnlyNodesWithMatchingNames()
    {
        var root = CreateDescriptor(
            "Root",
            CreateDescriptor(
                "Applications",
                CreateDescriptor("appsettings.json"),
                CreateDescriptor("Logs")),
            CreateDescriptor(
                "Docs",
                CreateDescriptor("README.md"),
                CreateDescriptor("app-notes.txt")));

        var count = NameFilterMatchCounter.CountMatchesUnderRoot(root, "app");

        Assert.Equal(3, count);
    }

    [Fact]
    public void CountMatchesUnderRoot_ReturnsZeroForEmptyQuery()
    {
        var root = CreateDescriptor("Root", CreateDescriptor("App"));

        var count = NameFilterMatchCounter.CountMatchesUnderRoot(root, string.Empty);

        Assert.Equal(0, count);
    }

    private static TreeNodeDescriptor CreateDescriptor(string name, params TreeNodeDescriptor[] children)
    {
        return new TreeNodeDescriptor(
            DisplayName: name,
            FullPath: $"C:\\{name}",
            IsDirectory: true,
            IsAccessDenied: false,
            IconKey: "icon",
            Children: children);
    }
}
