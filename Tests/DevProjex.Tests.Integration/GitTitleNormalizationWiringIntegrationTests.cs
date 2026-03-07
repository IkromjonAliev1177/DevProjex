namespace DevProjex.Tests.Integration;

public sealed class GitTitleNormalizationWiringIntegrationTests
{
    [Fact]
    public void MainWindow_UpdateTitle_NormalizesRepositoryUrlBeforeDisplay()
    {
        var body = ReadUpdateTitleBody();

        Assert.Contains(
            "RepositoryWebPathPresentationService.NormalizeForDisplay(_currentRepositoryUrl!);",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_viewModel.Title = $\"{MainWindowViewModel.BaseTitle} - {_currentRepositoryUrl}{branchDisplay}\";",
            body,
            StringComparison.Ordinal);
    }

    private static string ReadUpdateTitleBody()
    {
        var content = ReadMainWindowCode();
        var start = content.IndexOf("private void UpdateTitle()", StringComparison.Ordinal);
        var end = content.IndexOf("private IgnoreRules BuildIgnoreRules(", StringComparison.Ordinal);

        Assert.True(start >= 0, "UpdateTitle method not found.");
        Assert.True(end > start, "UpdateTitle method boundary not found.");

        return content.Substring(start, end - start);
    }

    private static string ReadMainWindowCode()
    {
        var repoRoot = FindRepositoryRoot();
        var file = Path.Combine(repoRoot, "Apps", "Avalonia", "DevProjex.Avalonia", "MainWindow.axaml.cs");
        return File.ReadAllText(file);
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                File.Exists(Path.Combine(dir, "DevProjex.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
