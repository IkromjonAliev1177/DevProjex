namespace DevProjex.Tests.UI;

public sealed class MainWindowPreviewUiTests
{
    [AvaloniaFact]
    public async Task LoadedProject_ShowsTreeAndSettingsBeforePreviewOpens()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            var viewModel = UiTestDriver.GetViewModel(window);
            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var settingsContainer = UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer");

            Assert.False(viewModel.IsPreviewMode);
            Assert.True(treeIsland.IsVisible);
            Assert.False(previewIsland.IsVisible);
            Assert.True(settingsContainer.IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewToggleButton_OpensPreviewBetweenTreeAndSettings()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var settingsContainer = UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer");

            var treeBounds = UiTestDriver.GetBoundsInWindow(treeIsland, window);
            var previewBounds = UiTestDriver.GetBoundsInWindow(previewIsland, window);
            var settingsBounds = UiTestDriver.GetBoundsInWindow(settingsContainer, window);

            Assert.True(viewModel.IsPreviewTreeVisible);
            Assert.True(previewIsland.IsVisible);
            Assert.InRange(previewBounds.Left - treeBounds.Right, 0, 12);
            Assert.InRange(settingsBounds.Left - previewBounds.Right, 0, 12);
            Assert.True(previewBounds.Width >= 320);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewCloseButton_ClosesPreviewAndRestoresTreeWorkspace()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.ClosePreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");

            Assert.False(viewModel.IsPreviewMode);
            Assert.True(treeIsland.IsVisible);
            Assert.False(previewIsland.IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task TreeHideButton_SwitchesPreviewToPreviewOnly()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");

            Assert.True(viewModel.IsPreviewOnlyMode);
            Assert.False(treeIsland.IsVisible);
            Assert.True(previewIsland.IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task FilterState_SurvivesPreviewOnlyCloseAndReopenCycle()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.OpenFilterAsync(window);

            var filterBar = UiTestDriver.GetRequiredControl<FilterBarView>(window, "FilterBar");
            await UiTestDriver.EnterTextAsync(window, Assert.IsType<TextBox>(filterBar.FilterBoxControl), "app");
            await UiTestDriver.WaitForFilterAppliedAsync(window, "app");

            await UiTestDriver.HidePreviewTreeAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var filterBarContainer = UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer");
            Assert.Equal("app", viewModel.NameFilter);
            Assert.False(filterBarContainer.IsVisible);

            await UiTestDriver.ClosePreviewAsync(window);

            await UiTestDriver.WaitForConditionAsync(
                window,
                () =>
                {
                    var currentViewModel = UiTestDriver.GetViewModel(window);
                    return currentViewModel.FilterVisible &&
                           currentViewModel.NameFilter == "app" &&
                           UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer").IsVisible;
                },
                "filter state to be restored after leaving preview-only");

            await UiTestDriver.OpenPreviewAsync(window);

            var restoredViewModel = UiTestDriver.GetViewModel(window);
            Assert.True(restoredViewModel.FilterVisible);
            Assert.Equal("app", restoredViewModel.NameFilter);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task SearchState_SurvivesPreviewOnlyCloseAndReopenCycle()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.OpenSearchAsync(window);

            var searchBar = UiTestDriver.GetRequiredControl<SearchBarView>(window, "SearchBar");
            await UiTestDriver.EnterTextAsync(window, Assert.IsType<TextBox>(searchBar.SearchBoxControl), "app");
            await UiTestDriver.WaitForSearchAppliedAsync(window, "app");

            await UiTestDriver.HidePreviewTreeAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var searchBarContainer = UiTestDriver.GetRequiredControl<Border>(window, "SearchBarContainer");
            Assert.Equal("app", viewModel.SearchQuery);
            Assert.False(searchBarContainer.IsVisible);

            await UiTestDriver.ClosePreviewAsync(window);

            await UiTestDriver.WaitForConditionAsync(
                window,
                () =>
                {
                    var currentViewModel = UiTestDriver.GetViewModel(window);
                    return currentViewModel.SearchVisible &&
                           currentViewModel.SearchQuery == "app" &&
                           UiTestDriver.GetRequiredControl<Border>(window, "SearchBarContainer").IsVisible;
                },
                "search state to be restored after leaving preview-only");

            await UiTestDriver.OpenPreviewAsync(window);

            var restoredViewModel = UiTestDriver.GetViewModel(window);
            Assert.True(restoredViewModel.SearchVisible);
            Assert.Equal("app", restoredViewModel.SearchQuery);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "SearchBarContainer").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task StickyPath_RemainsHiddenUntilPreviewScrollReachesFirstFileSection()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            var treeAndContentModeButton = UiTestDriver.GetRequiredControl<Button>(window, "PreviewTreeAndContentModeButton");
            await UiTestDriver.ClickAsync(window, treeAndContentModeButton);
            await UiTestDriver.WaitForPreviewReadyAsync(window);

            var stickyHeader = UiTestDriver.GetRequiredControl<Border>(window, "PreviewStickyHeaderContainer");
            var stickyHeaderText = UiTestDriver.GetRequiredControl<TextBlock>(window, "PreviewStickyHeaderText");
            Assert.False(stickyHeader.IsVisible);

            await UiTestDriver.ScrollPreviewUntilStickyHeaderVisibleAsync(window);

            Assert.True(stickyHeader.IsVisible);
            Assert.False(string.IsNullOrWhiteSpace(stickyHeaderText.Text));
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task SpaceKey_DoesNotReinvokeLastToolbarButtonAfterPreviewButtonClick()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            var previewToggleButton = UiTestDriver.GetRequiredTopMenuControl<Button>(window, "PreviewToggleButton");
            await UiTestDriver.ClickAsync(window, previewToggleButton);
            await UiTestDriver.WaitForPreviewReadyAsync(window);

            await UiTestDriver.PressKeyAsync(window, Key.Space);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 12);

            Assert.True(UiTestDriver.GetViewModel(window).IsPreviewMode);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewToggleButton_WhenClickedTwice_ClosesPreviewWorkspace()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.TogglePreviewViaToolbarAsync(window);
            await UiTestDriver.WaitForPreviewClosedAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.False(viewModel.IsPreviewMode);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewModeButtons_SwitchBetweenTreeContentAndCombinedModes()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            await UiTestDriver.SwitchPreviewModeAsync(window, PreviewContentMode.Content);
            Assert.True(UiTestDriver.GetViewModel(window).IsPreviewContentSelected);

            await UiTestDriver.SwitchPreviewModeAsync(window, PreviewContentMode.TreeAndContent);
            Assert.True(UiTestDriver.GetViewModel(window).IsPreviewTreeAndContentSelected);

            await UiTestDriver.SwitchPreviewModeAsync(window, PreviewContentMode.Tree);
            Assert.True(UiTestDriver.GetViewModel(window).IsPreviewTreeSelected);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOnlyCloseButton_RestoresPlainTreeWorkspaceWhenNoSuspendedTools()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);
            await UiTestDriver.ClosePreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.False(viewModel.IsPreviewMode);
            Assert.False(viewModel.SearchVisible);
            Assert.False(viewModel.FilterVisible);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland").IsVisible);
            Assert.False(UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOpen_PreservesExistingFilterState()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenFilterAsync(window);

            var filterBar = UiTestDriver.GetRequiredControl<FilterBarView>(window, "FilterBar");
            await UiTestDriver.EnterTextAsync(window, Assert.IsType<TextBox>(filterBar.FilterBoxControl), "app");
            await UiTestDriver.WaitForFilterAppliedAsync(window, "app");

            await UiTestDriver.OpenPreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.True(viewModel.FilterVisible);
            Assert.Equal("app", viewModel.NameFilter);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOpen_PreservesExistingSearchState()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenSearchAsync(window);

            var searchBar = UiTestDriver.GetRequiredControl<SearchBarView>(window, "SearchBar");
            await UiTestDriver.EnterTextAsync(window, Assert.IsType<TextBox>(searchBar.SearchBoxControl), "app");
            await UiTestDriver.WaitForSearchAppliedAsync(window, "app");

            await UiTestDriver.OpenPreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.True(viewModel.SearchVisible);
            Assert.Equal("app", viewModel.SearchQuery);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "SearchBarContainer").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task StickyPath_TextChangesWhenScrollingAcrossDifferentSections()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.SwitchPreviewModeAsync(window, PreviewContentMode.TreeAndContent);

            await UiTestDriver.WaitForConditionAsync(
                window,
                () => UiTestDriver.GetViewModel(window).PreviewDocument is { Sections.Count: > 1 },
                "preview document with multiple file sections");

            await UiTestDriver.ScrollPreviewUntilStickyHeaderVisibleAsync(window);

            var stickyHeaderText = UiTestDriver.GetRequiredControl<TextBlock>(window, "PreviewStickyHeaderText");
            var firstStickyText = stickyHeaderText.Text;
            Assert.False(string.IsNullOrWhiteSpace(firstStickyText));

            await UiTestDriver.ScrollPreviewUntilStickyHeaderTextChangesAsync(window, firstStickyText!);

            Assert.False(string.IsNullOrWhiteSpace(stickyHeaderText.Text));
            Assert.NotEqual(firstStickyText, stickyHeaderText.Text);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }
}
