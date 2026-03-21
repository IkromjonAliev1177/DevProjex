using DevProjex.Application.Preview;

namespace DevProjex.Tests.Unit;

public sealed class PreviewDocumentSectionLookupTests
{
    [Fact]
    public void FindContainingSection_ReturnsMatchingSection()
    {
        var sections = new[]
        {
            new PreviewDocumentSection("alpha.txt", 1, 4, 1, 3),
            new PreviewDocumentSection("beta.txt", 7, 9, 7, 9)
        };

        var section = PreviewDocumentSectionLookup.FindContainingSection(sections, 8);

        Assert.NotNull(section);
        Assert.Equal("beta.txt", section.DisplayPath);
    }

    [Fact]
    public void FindContainingOrNextSection_ReturnsUpcomingSection_ForSeparatorGap()
    {
        var sections = new[]
        {
            new PreviewDocumentSection("alpha.txt", 1, 4, 1, 3),
            new PreviewDocumentSection("beta.txt", 7, 9, 7, 9)
        };

        var section = PreviewDocumentSectionLookup.FindContainingOrNextSection(sections, 5);

        Assert.NotNull(section);
        Assert.Equal("beta.txt", section.DisplayPath);
    }
}
