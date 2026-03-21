namespace DevProjex.Tests.UI;

[Collection(UiWorkspaceCollection.Name)]
public sealed class MainWindowRepeatedCyclesUiTests(UiWorkspaceFixture workspace)
{
    [AvaloniaFact]
    public async Task PreviewWorkspace_RepeatedOpenCloseCyclesRemainStable()
    {
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(workspace.Project);

        try
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                await UiTestDriver.OpenPreviewAsync(window);
                Assert.True(UiTestDriver.GetViewModel(window).IsPreviewMode);

                await UiTestDriver.ClosePreviewAsync(window);
                Assert.False(UiTestDriver.GetViewModel(window).IsPreviewMode);
                Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland").IsVisible);
                Assert.False(UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland").IsVisible);
            }
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewTreeHide_RepeatedCyclesRemainStable()
    {
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(workspace.Project);

        try
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                await UiTestDriver.OpenPreviewAsync(window);
                await UiTestDriver.HidePreviewTreeAsync(window);
                Assert.True(UiTestDriver.GetViewModel(window).IsPreviewOnlyMode);

                await UiTestDriver.ClosePreviewAsync(window);
                Assert.False(UiTestDriver.GetViewModel(window).IsPreviewMode);
                Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "TreeIsland").IsVisible);
            }
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewWorkspace_RepeatedOpenCloseCyclesWithCollapsedSettingsRemainStable()
    {
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(workspace.Project);

        try
        {
            await UiTestDriver.PressKeyAsync(window, Key.P, RawInputModifiers.Control);
            await UiTestDriver.WaitForSettingsVisibilityAsync(window, visible: false);

            for (var iteration = 0; iteration < 3; iteration++)
            {
                await UiTestDriver.OpenPreviewAsync(window);
                Assert.True(UiTestDriver.GetViewModel(window).IsPreviewMode);
                Assert.False(UiTestDriver.GetViewModel(window).SettingsVisible);

                await UiTestDriver.ClosePreviewAsync(window);
                Assert.False(UiTestDriver.GetViewModel(window).IsPreviewMode);
                Assert.False(UiTestDriver.GetViewModel(window).SettingsVisible);
                Assert.False(UiTestDriver.IsActuallyVisibleHorizontally(
                    UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer")));
            }
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOnly_ResetZoomAffectsPreviewTargetOnly()
    {
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(workspace.Project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            var previewIsland = UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland");
            var previewPoint = UiTestDriver.GetControlCenter(previewIsland, window);

            window.MouseMove(previewPoint, RawInputModifiers.None);
            window.MouseWheel(previewPoint, new Vector(0, 1), RawInputModifiers.Control);
            await UiTestDriver.WaitForSettledFramesAsync(frameCount: 6);

            Assert.True(viewModel.PreviewFontSize > MainWindowViewModel.DefaultPreviewFontSize);

            await UiTestDriver.PressKeyAsync(window, Key.D0, RawInputModifiers.Control);

            Assert.Equal(MainWindowViewModel.DefaultPreviewFontSize, viewModel.PreviewFontSize);
            Assert.Equal(MainWindowViewModel.DefaultTreeFontSize, viewModel.TreeFontSize);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }
}
