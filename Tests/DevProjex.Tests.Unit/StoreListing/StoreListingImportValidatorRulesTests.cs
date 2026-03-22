using DevProjex.Tests.Shared.StoreListing;

namespace DevProjex.Tests.Unit.StoreListing;

public sealed class StoreListingImportValidatorRulesTests
{
    [Fact]
    public void ValidateImportFolder_Fails_WhenCsvUsesUtf8Bom()
    {
        var builder = new StoreListingValidationTestBuilder();
        var fixture = builder.Build(withUtf8Bom: true);
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI004");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenCsvUsesLfOnlyLineEndings()
    {
        var builder = new StoreListingValidationTestBuilder();
        var fixture = builder.Build(withLfOnly: true);
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI005");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenKeywordBudgetExceedsTwentyOneWords()
    {
        // This is the exact Partner Center quirk that burned us in production:
        // seven keywords can still be invalid if the total word count goes above 21.
        var builder = new StoreListingValidationTestBuilder()
            .UseKeywordBudgetOverflow();

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI013");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenKeywordIsLongerThanFortyCharacters()
    {
        var builder = new StoreListingValidationTestBuilder()
            .UseLongKeyword();

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI012");
    }

    [Fact]
    public void ValidateImportFolder_Fails_WhenRequiredFieldIsMissingForLocale()
    {
        var builder = new StoreListingValidationTestBuilder()
            .RemoveValue("Description", "fr-fr");

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.Contains(report.Errors, issue => issue.Code == "SLI010" && issue.Message.Contains("fr-fr", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateImportFolder_Allows_ExactlyTwentyOneKeywordWords()
    {
        var builder = new StoreListingValidationTestBuilder()
            .WithValue("SearchTerm1", "en-us", "project context for ai")
            .WithValue("SearchTerm2", "en-us", "project tree")
            .WithValue("SearchTerm3", "en-us", "copy code")
            .WithValue("SearchTerm4", "en-us", "export structure")
            .WithValue("SearchTerm5", "en-us", "ai codebase")
            .WithValue("SearchTerm6", "en-us", "git viewer")
            .WithValue("SearchTerm7", "en-us", "claude context");

        var fixture = builder.Build();
        using var tempDirectory = fixture.TempDirectory;

        var report = StoreListingImportValidator.ValidateImportFolder(
            fixture.ImportFolderPath,
            fixture.ImportCsvPath,
            fixture.TemplateCsvPath,
            new StoreListingValidationOptions());

        Assert.DoesNotContain(report.Errors, issue => issue.Code == "SLI013");
    }
}
