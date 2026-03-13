namespace DevProjex.Tests.Unit.Helpers;

public sealed class HelpContentProviderTests
{
    [Fact]
    public void GetHelpBody_ReturnsNonEmptyEnglishContent()
    {
        var provider = new HelpContentProvider();

        var result = provider.GetHelpBody(AppLanguage.En);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public void GetHelpBody_ReturnsDifferentContentForDifferentLanguages_WhenResourcesExist()
    {
        var provider = new HelpContentProvider();

        var english = provider.GetHelpBody(AppLanguage.En);
        var russian = provider.GetHelpBody(AppLanguage.Ru);

        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(russian));
        Assert.NotEqual(english, russian);
    }
}