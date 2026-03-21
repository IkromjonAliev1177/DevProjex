namespace DevProjex.Tests.UI;

public sealed class MainWindowWorkspaceInteractionUiTests
{
    [AvaloniaFact]
    public async Task FilterButton_RemainsEnabledButIgnoredInPreviewOnly()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);

            var filterToggleButton = UiTestDriver.GetRequiredTopMenuControl<Button>(window, "FilterToggleButton");
            Assert.True(filterToggleButton.IsEnabled);

            await UiTestDriver.ClickAsync(window, filterToggleButton);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 8);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.False(viewModel.FilterVisible);
            Assert.False(UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task TreePreviewSplitter_DragResizesTreeAndPreviewPanes()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var splitter = UiTestDriver.GetRequiredControl<Border>(window, "TreePreviewSplitter");

            var treeWidthBefore = UiTestDriver.GetBoundsInWindow(treeIsland, window).Width;
            var previewWidthBefore = UiTestDriver.GetBoundsInWindow(previewIsland, window).Width;

            await UiTestDriver.DragAsync(window, splitter, deltaX: 120);

            var treeWidthAfter = UiTestDriver.GetBoundsInWindow(treeIsland, window).Width;
            var previewWidthAfter = UiTestDriver.GetBoundsInWindow(previewIsland, window).Width;

            Assert.True(treeWidthAfter > treeWidthBefore + 10);
            Assert.True(previewWidthAfter < previewWidthBefore - 10);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewSettingsSplitter_DragResizesSettingsPaneWithinConfiguredBounds()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var splitter = UiTestDriver.GetRequiredControl<Border>(window, "PreviewSettingsSplitter");
            var settingsContainer = UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer");
            var widthBefore = UiTestDriver.GetBoundsInWindow(settingsContainer, window).Width;

            await UiTestDriver.DragAsync(window, splitter, deltaX: 220);
            var widthCollapsed = UiTestDriver.GetBoundsInWindow(settingsContainer, window).Width;

            await UiTestDriver.DragAsync(window, splitter, deltaX: -140);
            var widthExpanded = UiTestDriver.GetBoundsInWindow(settingsContainer, window).Width;

            var diagnostic = $"Before={widthBefore:F2}, Collapsed={widthCollapsed:F2}, Expanded={widthExpanded:F2}";
            Assert.True(widthCollapsed < widthBefore - 1, diagnostic);
            Assert.InRange(widthCollapsed, 240, 321);
            Assert.True(widthExpanded > widthBefore + 1, diagnostic);
            Assert.True(widthExpanded > widthCollapsed + 5, diagnostic);
            Assert.InRange(widthExpanded, widthCollapsed, 321);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task CtrlWheelOverTree_ChangesOnlyTreeZoomInsidePreviewWorkspace()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var point = UiTestDriver.GetControlCenter(treeIsland, window);
            var treeBefore = viewModel.TreeFontSize;
            var previewBefore = viewModel.PreviewFontSize;

            window.MouseMove(point, RawInputModifiers.None);
            window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.Control);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 6);

            Assert.True(viewModel.TreeFontSize > treeBefore);
            Assert.Equal(previewBefore, viewModel.PreviewFontSize);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task CtrlWheelOverPreview_ChangesOnlyPreviewZoomInsidePreviewWorkspace()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var point = UiTestDriver.GetControlCenter(previewIsland, window);
            var treeBefore = viewModel.TreeFontSize;
            var previewBefore = viewModel.PreviewFontSize;

            window.MouseMove(point, RawInputModifiers.None);
            window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.Control);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 6);

            Assert.Equal(treeBefore, viewModel.TreeFontSize);
            Assert.True(viewModel.PreviewFontSize > previewBefore);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task CtrlZero_ResetsBothZoomTargetsWhenPreviewShowsTreeAndContent()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var treePoint = UiTestDriver.GetControlCenter(treeIsland, window);
            var previewPoint = UiTestDriver.GetControlCenter(previewIsland, window);

            window.MouseMove(treePoint, RawInputModifiers.None);
            window.MouseWheel(treePoint, new Vector(0, 1), RawInputModifiers.Control);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 4);

            window.MouseMove(previewPoint, RawInputModifiers.None);
            window.MouseWheel(previewPoint, new Vector(0, 1), RawInputModifiers.Control);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 4);

            await UiTestDriver.PressKeyAsync(window, Key.D0, RawInputModifiers.Control);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.Equal(MainWindowViewModel.DefaultTreeFontSize, viewModel.TreeFontSize);
            Assert.Equal(MainWindowViewModel.DefaultPreviewFontSize, viewModel.PreviewFontSize);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task TreePreviewSplitter_RespectsMinimumWidthsAtExtremeDrag()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var treeIsland = UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland");
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var splitter = UiTestDriver.GetRequiredControl<Border>(window, "TreePreviewSplitter");

            await UiTestDriver.DragAsync(window, splitter, deltaX: 2_000);
            var treeExpandedWidth = UiTestDriver.GetBoundsInWindow(treeIsland, window).Width;
            var previewCollapsedWidth = UiTestDriver.GetBoundsInWindow(previewIsland, window).Width;

            await UiTestDriver.DragAsync(window, splitter, deltaX: -2_000);
            var treeCollapsedWidth = UiTestDriver.GetBoundsInWindow(treeIsland, window).Width;
            var previewExpandedWidth = UiTestDriver.GetBoundsInWindow(previewIsland, window).Width;

            Assert.True(treeExpandedWidth >= 418);
            Assert.True(previewCollapsedWidth >= 320);
            Assert.True(treeCollapsedWidth >= 418);
            Assert.True(previewExpandedWidth >= 320);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewSettingsSplitter_RespectsHardBoundsAtExtremeDrag()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            var splitter = UiTestDriver.GetRequiredControl<Border>(window, "PreviewSettingsSplitter");
            var settingsContainer = UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer");

            await UiTestDriver.DragAsync(window, splitter, deltaX: 2_000);
            var collapsedWidth = UiTestDriver.GetBoundsInWindow(settingsContainer, window).Width;

            await UiTestDriver.DragAsync(window, splitter, deltaX: -2_000);
            var expandedWidth = UiTestDriver.GetBoundsInWindow(settingsContainer, window).Width;

            Assert.InRange(collapsedWidth, 240, 321);
            Assert.InRange(expandedWidth, collapsedWidth, 421);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }
}
