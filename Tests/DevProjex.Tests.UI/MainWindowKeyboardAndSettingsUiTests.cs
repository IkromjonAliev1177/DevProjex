namespace DevProjex.Tests.UI;

public sealed class MainWindowKeyboardAndSettingsUiTests
{
    [AvaloniaFact]
    public async Task CtrlB_TogglesPreviewWorkspace()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.PressKeyAsync(window, Key.B, RawInputModifiers.Control);
            await UiTestDriver.WaitForPreviewReadyAsync(window);
            Assert.True(UiTestDriver.GetViewModel(window).IsPreviewMode);

            await UiTestDriver.PressKeyAsync(window, Key.B, RawInputModifiers.Control);
            await UiTestDriver.WaitForPreviewClosedAsync(window);
            Assert.False(UiTestDriver.GetViewModel(window).IsPreviewMode);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task CtrlP_TogglesSettingsWhilePreviewStaysVisible()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.OpenPreviewAsync(window);

            await UiTestDriver.PressKeyAsync(window, Key.P, RawInputModifiers.Control);
            await UiTestDriver.WaitForSettingsVisibilityAsync(window, visible: false);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.True(viewModel.IsPreviewMode);
            Assert.False(viewModel.SettingsVisible);
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland").IsVisible);

            await UiTestDriver.PressKeyAsync(window, Key.P, RawInputModifiers.Control);
            await UiTestDriver.WaitForSettingsVisibilityAsync(window, visible: true);

            Assert.True(UiTestDriver.GetViewModel(window).SettingsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOpen_WhenSettingsAreHidden_DoesNotReopenSettings()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.PressKeyAsync(window, Key.P, RawInputModifiers.Control);
            await UiTestDriver.WaitForSettingsVisibilityAsync(window, visible: false);

            await UiTestDriver.OpenPreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.True(viewModel.IsPreviewMode);
            Assert.False(viewModel.SettingsVisible);
            Assert.False(UiTestDriver.IsActuallyVisibleHorizontally(
                UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer")));
            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "PreviewIsland").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewClose_PreservesCollapsedSettingsState()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.PressKeyAsync(window, Key.P, RawInputModifiers.Control);
            await UiTestDriver.WaitForSettingsVisibilityAsync(window, visible: false);

            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.ClosePreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.False(viewModel.IsPreviewMode);
            Assert.False(viewModel.SettingsVisible);
            Assert.False(UiTestDriver.IsActuallyVisibleHorizontally(
                UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer")));
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task PreviewOnlyClose_PreservesCollapsedSettingsState()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.PressKeyAsync(window, Key.P, RawInputModifiers.Control);
            await UiTestDriver.WaitForSettingsVisibilityAsync(window, visible: false);

            await UiTestDriver.OpenPreviewAsync(window);
            await UiTestDriver.HidePreviewTreeAsync(window);
            await UiTestDriver.ClosePreviewAsync(window);

            var viewModel = UiTestDriver.GetViewModel(window);
            Assert.False(viewModel.IsPreviewMode);
            Assert.False(viewModel.SettingsVisible);
            Assert.False(UiTestDriver.IsActuallyVisibleHorizontally(
                UiTestDriver.GetRequiredControl<Border>(window, "SettingsContainer")));
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }

    [AvaloniaFact]
    public async Task CtrlShiftN_OpensFilterHotkeyPath()
    {
        using var project = UiTestProject.CreateDefault();
        var window = await UiTestDriver.CreateLoadedMainWindowAsync(project);

        try
        {
            await UiTestDriver.PressKeyAsync(window, Key.N, RawInputModifiers.Control | RawInputModifiers.Shift);
            await UiTestDriver.WaitForConditionAsync(
                window,
                () => UiTestDriver.GetViewModel(window).FilterVisible,
                "filter bar to open via Ctrl+Shift+N");

            Assert.True(UiTestDriver.GetRequiredControl<Border>(window, "FilterBarContainer").IsVisible);
        }
        finally
        {
            await UiTestDriver.CloseWindowAsync(window);
        }
    }
}
