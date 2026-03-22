using DevProjex.Tests.Shared.StoreListing;

namespace DevProjex.Tests.Unit.StoreListing;

public sealed class StoreListingImportValidatorAssetTests
{
    [Fact]
    public void ValidateImportFolder_Fails_WhenAssetUsesBackslashes()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseBackslashAssetPath();

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI015");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenAssetDoesNotUseImportFolderPrefix()
    {
        var builder = new StoreListingValidationTestBuilder()
            .WithoutImportFolderPrefix();

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI018");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenLegacyTjSuffixAppearsForTajikLocale()
    {
        // The locale code is tg-cyrl-tj, but the screenshot filename must still be TG.png.
        // We keep this as a dedicated test because the bug is easy to reintroduce by hand.
        var builder = new StoreListingValidationTestBuilder()
            .UseLegacyTjSuffix();

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI017");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenExpectedScreenshotFileIsMissing()
    {
        var builder = new StoreListingValidationTestBuilder();
        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        File.Delete(Path.Combine(fixture.ImportFolderPath, "Screenshots", "1", "EN.png"));

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI019");
    }

    [Fact]
    public void ValidateImportFolder_Passes_WhenScreenshotCoverageUsesTwoSlotsConsistently()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseConsistentScreenshotCoverage(1, 2);

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.DoesNotContain(report.Errors, issue => issue.Code is "SLI014" or "SLI021" or "SLI022");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenScreenshotCoverageDiffersBetweenLocales()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseConsistentScreenshotCoverage(1, 2, 3)
            .UseInconsistentScreenshotCoverage("fr-fr", 1, 2);

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI022");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenScreenshotSlotsContainAGap()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseConsistentScreenshotCoverage(1, 3);

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI021");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenLocaleHasNoScreenshots()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseInconsistentScreenshotCoverage("de-de");

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI014" && issue.Message.Contains("de-de", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateImportFolder_Allows_RemoteStoreLogoUrlsFromPartnerCenterExports()
    {
        var builder = new StoreListingValidationTestBuilder()
            .WithValue("StoreLogo300x300", "de-de", "https://developer.microsoft.com/store-assets/devprojex/logo.png");

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.DoesNotContain(report.Errors, issue => issue.Code == "SLI018" || issue.Code == "SLI019");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenAssetEscapesImportFolder()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseEscapingAssetPath();

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI016");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenPngAssetIsCorrupted()
    {
        var builder = new StoreListingValidationTestBuilder()
            .CorruptPngAsset("Screenshots/1/EN.png");

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI020");
    }
}
