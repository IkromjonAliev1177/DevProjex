using DevProjex.Tests.Shared.StoreListing;

namespace DevProjex.Tests.Unit.StoreListing;

public sealed class StoreListingCsvDocumentTests
{
    [Fact]
    public void Load_ParsesMultilineQuotedCells_AndPreservesHeaderOrder()
    {
        using var tempDirectory = new TemporaryDirectory();
        var csvPath = tempDirectory.CreateFile(
            "listing.csv",
            """
            Field,ID,Type (Тип),default,en-us
            Description,2,Текст,,"Line 1
            Line 2"
            Title,4,Текст,,DevProjex
            """);

        var document = StoreListingCsvDocument.Load(csvPath);

        Assert.Equal(["Field", "ID", "Type (Тип)", "default", "en-us"], document.Headers);
        Assert.Equal(2, document.Rows.Count);
        var description = document.RowsByField["Description"].GetValue("en-us").Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal("Line 1\nLine 2", description);
    }

    [Fact]
    public void Load_BuildsFieldIndex_ForFastRuleBasedValidation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var csvPath = tempDirectory.CreateFile(
            "listing.csv",
            """
            Field,ID,Type (Тип),default,en-us
            Title,4,Текст,,DevProjex
            ShortDescription,8,Текст,,Short text
            """);

        var document = StoreListingCsvDocument.Load(csvPath);

        Assert.Equal("DevProjex", document.RowsByField["Title"].GetValue("en-us"));
        Assert.Equal("Short text", document.RowsByField["ShortDescription"].GetValue("en-us"));
    }
}
