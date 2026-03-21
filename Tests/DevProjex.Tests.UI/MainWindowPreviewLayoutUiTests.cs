namespace DevProjex.Tests.UI;

public sealed class MainWindowPreviewLayoutUiTests
{
    [AvaloniaFact]
    public async Task PreviewOnlyClose_RestoresActiveFilterAfterToolbarCycle()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenFilterAsync(window);

            var filterBar = UiTestDriver.GetRequiredControl<FilterBarView>(window, "FilterBar");
            await UiTestDriver.EnterTextAsync(window, Assert.IsType<TextBox>(filterBar.FilterBoxControl), "preview");
            await UiTestDriver.WaitForFilterAppliedAsync(window, "preview");

            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);
            await UiTestDriver.ClosePreviewAsync(window);

            await UiTestDriver.WaitForConditionAsync(
                window,
                () =>
                {
                    var viewModel = UiTestDriver.GetViewModel(window);
                    return viewModel.FilterVisible &&
                           viewModel.NameFilter == "preview" &&
                           UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer").IsVisible;
                },
                "filter state to be restored after preview-only close");
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOnlyClose_RestoresActiveSearchAfterToolbarCycle()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenSearchAsync(window);

            var searchBar = UiTestDriver.GetRequiredControl<SearchBarView>(window, "SearchBar");
            await UiTestDriver.EnterTextAsync(window, Assert.IsType<TextBox>(searchBar.SearchBoxControl), "preview");
            await UiTestDriver.WaitForSearchAppliedAsync(window, "preview");

            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);
            await UiTestDriver.ClosePreviewAsync(window);

            await UiTestDriver.WaitForConditionAsync(
                window,
                () =>
                {
                    var viewModel = UiTestDriver.GetViewModel(window);
                    return viewModel.SearchVisible &&
                           viewModel.SearchQuery == "preview" &&
                           UiTestDriver.GetRequiredControl<Border>(window, "SearchBarContainer").IsVisible;
                },
                "search state to be restored after preview-only close");
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }
}
