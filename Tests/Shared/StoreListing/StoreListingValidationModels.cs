namespace DevProjex.Tests.Shared.StoreListing;

internal sealed record StoreListingValidationIssue(string Code, string Message);

internal sealed class StoreListingValidationReport
{
    private readonly List<StoreListingValidationIssue> _errors = [];

    internal IReadOnlyList<StoreListingValidationIssue> Errors => _errors;

    internal bool HasErrors => _errors.Count > 0;

    internal void AddError(string code, string message)
    {
        _errors.Add(new StoreListingValidationIssue(code, message));
    }
}

internal sealed class StoreListingValidationOptions
{
    // Partner Center import was proven to be sensitive to BOM in this repository.
    // The default stays strict so accidental editor changes fail fast in CI.
    internal bool RequireUtf8WithoutBom { get; init; } = true;

    // Mixed or LF-only endings are easy to introduce when tools edit CSV on multiple platforms.
    // We keep CRLF as the canonical format because it is what the real successful import used.
    internal bool RequireCrLfLineEndings { get; init; } = true;

    // Local asset paths are expected to use the ImportFolder/... convention.
    // Remote Partner Center URLs remain allowed and are handled separately by the validator.
    internal bool RequireImportFolderPrefixInAssetPaths { get; init; } = true;

    // The export template is the real contract. If Microsoft changes the schema, we want
    // validation to fail before someone uploads a silently drifted CSV to Partner Center.
    internal bool RequireTemplateSchemaMatch { get; init; } = true;

    // Microsoft requires at least one screenshot. We keep the minimum configurable so the
    // project can evolve without hardcoding "exactly 5" forever.
    internal int MinimumScreenshotsPerLocale { get; init; } = 1;

    // All locales should expose the same screenshot slots so the listing stays visually
    // consistent and does not drift language-by-language over time.
    internal bool RequireConsistentScreenshotCoverage { get; init; } = true;

    // If DesktopScreenshot3 exists, DesktopScreenshot1 and DesktopScreenshot2 should also
    // exist for that locale. This prevents accidental gaps such as 1,2,4.
    internal bool RequireContiguousScreenshotSlots { get; init; } = true;
}
