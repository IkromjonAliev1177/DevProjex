namespace DevProjex.Tests.Integration;

public sealed class ContentMetricsWiringIntegrationTests
{
    [Fact]
    public void MainWindow_CalculateContentMetrics_UsesExportPathMapperForDisplayedPaths()
    {
        var body = SliceMainWindow(
            "private ExportOutputMetrics CalculateContentMetrics(",
            "private void UpdateStatusBarMetrics(");

        Assert.Contains("var pathMapper = CreateExportPathPresentation()?.MapFilePath;", body, StringComparison.Ordinal);
        Assert.Contains("var displayPath = MapExportDisplayPath(path, pathMapper);", body, StringComparison.Ordinal);
        Assert.Contains("Path: displayPath,", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CalculateContentMetrics_PropagatesEstimatedMetricsFlag()
    {
        var body = SliceMainWindow(
            "private ExportOutputMetrics CalculateContentMetrics(",
            "private void UpdateStatusBarMetrics(");

        Assert.Contains("IsEstimated: metrics.IsEstimated,", body, StringComparison.Ordinal);
    }

    private static string SliceMainWindow(string startMarker, string endMarker)
    {
        var repoRoot = FindRepositoryRoot();
        var file = Path.Combine(repoRoot, "Apps", "Avalonia", "DevProjex.Avalonia", "MainWindow.axaml.cs");
        var content = File.ReadAllText(file);

        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");

        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found after start marker: {endMarker}");

        return content[start..end];
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
