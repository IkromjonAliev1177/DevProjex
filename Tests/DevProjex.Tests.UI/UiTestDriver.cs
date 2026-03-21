namespace DevProjex.Tests.UI;

internal static class UiTestDriver
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    public static async Task<MainWindow> CreateLoadedMainWindowAsync(UiTestProject project)
    {
        var options = new CommandLineOptions(project.RootPath, AppLanguage.En, false);
        var services = AvaloniaCompositionRoot.CreateDefault(options);
        var window = new MainWindow(options, services)
        {
            Width = 1500,
            Height = 920
        };

        window.Show();

        await WaitForConditionAsync(
            window,
            () => window.IsVisible,
            "main window to become visible");

        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                return viewModel.IsProjectLoaded &&
                       viewModel.TreeNodes.Count > 0 &&
                       !viewModel.StatusBusy;
            },
            "project to finish loading");

        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                if (!viewModel.SettingsVisible)
                    return true;

                var settingsContainer = GetRequiredControl<Border>(window, "SettingsContainer");
                return GetActualWidth(settingsContainer) >= 200;
            },
            "initial settings pane to become visually available");

        await WaitForSettledFramesAsync(frameCount: 24);
        return window;
    }

    public static async Task CloseWindowAsync(MainWindow window)
    {
        if (!window.IsVisible)
            return;

        window.Close();
        await WaitForSettledFramesAsync(frameCount: 6);
    }

    public static MainWindowViewModel GetViewModel(MainWindow window)
        => Assert.IsType<MainWindowViewModel>(window.DataContext);

    public static T GetRequiredControl<T>(MainWindow window, string name)
        where T : Control
    {
        var control = window.FindControl<T>(name);
        return Assert.IsType<T>(control);
    }

    public static T GetRequiredTopMenuControl<T>(MainWindow window, string name)
        where T : Control
    {
        var topMenuBar = GetRequiredControl<TopMenuBarView>(window, "TopMenuBar");
        var control = topMenuBar.FindControl<T>(name);
        return Assert.IsType<T>(control);
    }

    public static async Task ClickAsync(MainWindow window, Control control)
    {
        var clickPoint = GetControlCenter(control, window);
        window.MouseMove(clickPoint, RawInputModifiers.None);
        await WaitForSettledFramesAsync(frameCount: 2);
        window.MouseDown(clickPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(clickPoint, MouseButton.Left, RawInputModifiers.None);
        await WaitForSettledFramesAsync(frameCount: 4);
    }

    public static async Task DragAsync(MainWindow window, Control control, double deltaX)
    {
        var startPoint = GetControlCenter(control, window);
        var endPoint = new Point(startPoint.X + deltaX, startPoint.Y);
        window.MouseMove(startPoint, RawInputModifiers.None);
        await WaitForSettledFramesAsync(frameCount: 1);
        window.MouseDown(startPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);

        const int steps = 6;
        for (var step = 1; step <= steps; step++)
        {
            var t = step / (double)steps;
            var intermediatePoint = new Point(
                startPoint.X + ((endPoint.X - startPoint.X) * t),
                startPoint.Y);
            window.MouseMove(intermediatePoint, RawInputModifiers.LeftMouseButton);
            await WaitForSettledFramesAsync(frameCount: 1);
        }

        window.MouseUp(endPoint, MouseButton.Left, RawInputModifiers.None);
        await WaitForSettledFramesAsync(frameCount: 12);
    }

    public static async Task PressKeyAsync(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var physicalKey = key switch
        {
            Key.F => PhysicalKey.F,
            Key.B => PhysicalKey.B,
            Key.P => PhysicalKey.P,
            Key.N => PhysicalKey.N,
            Key.Escape => PhysicalKey.Escape,
            Key.Space => PhysicalKey.Space,
            Key.D0 => PhysicalKey.Digit0,
            _ => PhysicalKey.None
        };
        var keySymbol = physicalKey.ToQwertyKeySymbol(modifiers.HasFlag(RawInputModifiers.Shift));
        window.KeyPress(key, modifiers, physicalKey, keySymbol);
        await WaitForSettledFramesAsync(frameCount: 1);
        window.KeyRelease(key, modifiers, physicalKey, keySymbol);
        await WaitForSettledFramesAsync(frameCount: 3);
    }

    public static async Task EnterTextAsync(MainWindow window, TextBox textBox, string text)
    {
        await ClickAsync(window, textBox);
        window.KeyTextInput(text);
        await WaitForSettledFramesAsync(frameCount: 4);
    }

    public static async Task OpenPreviewAsync(MainWindow window)
    {
        var previewToggleButton = GetRequiredTopMenuControl<Button>(window, "PreviewToggleButton");
        await ClickAsync(window, previewToggleButton);
        await WaitForPreviewReadyAsync(window);
    }

    public static async Task TogglePreviewViaToolbarAsync(MainWindow window)
    {
        var previewToggleButton = GetRequiredTopMenuControl<Button>(window, "PreviewToggleButton");
        await ClickAsync(window, previewToggleButton);
    }

    public static async Task ClosePreviewAsync(MainWindow window)
    {
        var previewCloseButton = GetRequiredControl<Button>(window, "PreviewCloseButton");
        await ClickAsync(window, previewCloseButton);
        await WaitForPreviewClosedAsync(window);
    }

    public static async Task WaitForPreviewClosedAsync(MainWindow window)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                var previewIsland = GetRequiredControl<Border>(window, "PreviewIsland");
                return !viewModel.IsPreviewMode && !previewIsland.IsVisible;
            },
            "preview workspace to close");
        await WaitForSettledFramesAsync(frameCount: 18);
    }

    public static async Task HidePreviewTreeAsync(MainWindow window)
    {
        var treeHideButton = GetRequiredControl<Button>(window, "PreviewTreeHideButton");
        await ClickAsync(window, treeHideButton);
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                var treeIsland = GetRequiredControl<Border>(window, "TreeIsland");
                return viewModel.IsPreviewOnlyMode && !treeIsland.IsVisible;
            },
            "preview tree pane to collapse");
        await WaitForSettledFramesAsync(frameCount: 18);
    }

    public static async Task OpenFilterAsync(MainWindow window)
    {
        var filterToggleButton = GetRequiredTopMenuControl<Button>(window, "FilterToggleButton");
        await ClickAsync(window, filterToggleButton);
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                var filterBarContainer = GetRequiredControl<Border>(window, "FilterBarContainer");
                return viewModel.FilterVisible && filterBarContainer.IsVisible;
            },
            "filter bar to open");
        await WaitForSettledFramesAsync(frameCount: 10);
    }

    public static async Task OpenSearchAsync(MainWindow window)
    {
        await PressKeyAsync(window, Key.F, RawInputModifiers.Control);
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                var searchBarContainer = GetRequiredControl<Border>(window, "SearchBarContainer");
                return viewModel.SearchVisible && searchBarContainer.IsVisible;
            },
            "search bar to open");
        await WaitForSettledFramesAsync(frameCount: 10);
    }

    public static async Task WaitForSettingsVisibilityAsync(MainWindow window, bool visible)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                var settingsContainer = GetRequiredControl<Border>(window, "SettingsContainer");
                var isEffectivelyVisible = IsActuallyVisibleHorizontally(settingsContainer);

                return viewModel.SettingsVisible == visible &&
                       isEffectivelyVisible == visible;
            },
            $"settings visibility to become {visible}");

        await WaitForSettledFramesAsync(frameCount: 12);
    }

    public static async Task SwitchPreviewModeAsync(MainWindow window, PreviewContentMode mode)
    {
        var buttonName = mode switch
        {
            PreviewContentMode.Tree => "PreviewTreeModeButton",
            PreviewContentMode.Content => "PreviewContentModeButton",
            PreviewContentMode.TreeAndContent => "PreviewTreeAndContentModeButton",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        var button = GetRequiredControl<Button>(window, buttonName);
        await ClickAsync(window, button);

        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                return viewModel.SelectedPreviewContentMode == mode &&
                       !viewModel.IsPreviewLoading &&
                       viewModel.PreviewDocument is not null;
            },
            $"preview mode {mode} to become active");

        await WaitForSettledFramesAsync(frameCount: 12);
    }

    public static async Task WaitForPreviewReadyAsync(MainWindow window)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                var previewIsland = GetRequiredControl<Border>(window, "PreviewIsland");
                return viewModel.IsPreviewMode &&
                       previewIsland.IsVisible &&
                       !viewModel.IsPreviewLoading &&
                       viewModel.PreviewDocument is not null;
            },
            "preview workspace to become ready");
        await WaitForSettledFramesAsync(frameCount: 18);
    }

    public static async Task WaitForFilterAppliedAsync(MainWindow window, string query)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                return viewModel.FilterVisible &&
                       !viewModel.IsFilterInProgress &&
                       string.Equals(viewModel.NameFilter, query, StringComparison.Ordinal) &&
                       viewModel.FilterMatchCount > 0;
            },
            $"filter query '{query}' to finish");
        await WaitForSettledFramesAsync(frameCount: 6);
    }

    public static async Task WaitForSearchAppliedAsync(MainWindow window, string query)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                return viewModel.SearchVisible &&
                       !viewModel.IsSearchInProgress &&
                       string.Equals(viewModel.SearchQuery, query, StringComparison.Ordinal) &&
                       viewModel.SearchTotalMatches > 0;
            },
            $"search query '{query}' to finish");
        await WaitForSettledFramesAsync(frameCount: 6);
    }

    public static async Task ScrollPreviewUntilStickyHeaderVisibleAsync(MainWindow window)
    {
        var stickyHeaderContainer = GetRequiredControl<Border>(window, "PreviewStickyHeaderContainer");
        var scrollViewer = GetRequiredPreviewScrollViewer(window);

        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                return viewModel.PreviewDocument is { Sections.Count: > 0 };
            },
            "preview document with sections to become available");

        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var step = Math.Max(72, scrollViewer.Viewport.Height / 3);

        for (var offset = step; offset <= maxOffset + step; offset += step)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Min(maxOffset, offset));
            await WaitForSettledFramesAsync(frameCount: 3);
            if (stickyHeaderContainer.IsVisible)
                return;
        }

        throw new XunitException("Sticky preview path never became visible after scrolling through the preview content.");
    }

    public static async Task ScrollPreviewUntilStickyHeaderTextChangesAsync(MainWindow window, string previousText)
    {
        var stickyHeaderText = GetRequiredControl<TextBlock>(window, "PreviewStickyHeaderText");
        var scrollViewer = GetRequiredPreviewScrollViewer(window);
        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var step = Math.Max(96, scrollViewer.Viewport.Height / 3);

        for (var offset = scrollViewer.Offset.Y + step; offset <= maxOffset + step; offset += step)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, Math.Min(maxOffset, offset));
            await WaitForSettledFramesAsync(frameCount: 3);

            if (!string.IsNullOrWhiteSpace(stickyHeaderText.Text) &&
                !string.Equals(stickyHeaderText.Text, previousText, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new XunitException("Sticky preview path text never changed after scrolling through multiple preview sections.");
    }

    public static ScrollViewer GetRequiredPreviewScrollViewer(MainWindow window)
        => GetRequiredControl<ScrollViewer>(window, "PreviewTextScrollViewer");

    public static double GetActualWidth(Control control)
        => control.Bounds.Width;

    public static bool IsActuallyVisibleHorizontally(Control control)
        => control.IsVisible && GetActualWidth(control) > 0.5;

    public static Rect GetBoundsInWindow(Control control, TopLevel topLevel)
    {
        var origin = control.TranslatePoint(default, topLevel);
        if (!origin.HasValue)
            throw new XunitException($"Unable to translate control '{control.Name}' into top-level coordinates.");

        return new Rect(origin.Value, control.Bounds.Size);
    }

    public static Point GetControlCenter(Control control, TopLevel topLevel)
    {
        var bounds = GetBoundsInWindow(control, topLevel);
        return bounds.Center;
    }

    public static async Task WaitForConditionAsync(
        MainWindow window,
        Func<bool> predicate,
        string description,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            await WaitForSettledFramesAsync(frameCount: 2);
            if (predicate())
                return;

            await Task.Delay(15);
        }

        throw new XunitException($"Timed out waiting for {description}. Current state: {DescribeState(window)}");
    }

    public static async Task WaitForSettledFramesAsync(int frameCount)
    {
        for (var index = 0; index < frameCount; index++)
        {
            // Drive both dispatcher queues and the headless render timer so tests observe
            // the same visual state users would see after an animation or layout pass.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            await Task.Delay(6);
        }
    }

    private static string DescribeState(MainWindow window)
    {
        var viewModel = GetViewModel(window);
        var settingsContainer = window.FindControl<Border>("SettingsContainer");
        var settingsWidth = settingsContainer is null
            ? 0.0
            : GetActualWidth(settingsContainer);
        return string.Join(
            ", ",
            [
                $"Visible={window.IsVisible}",
                $"ProjectLoaded={viewModel.IsProjectLoaded}",
                $"TreeNodes={viewModel.TreeNodes.Count}",
                $"PreviewMode={viewModel.PreviewWorkspaceMode}",
                $"PreviewLoading={viewModel.IsPreviewLoading}",
                $"SettingsVisible={viewModel.SettingsVisible}",
                $"SettingsWidth={settingsWidth:F2}",
                $"SearchVisible={viewModel.SearchVisible}",
                $"FilterVisible={viewModel.FilterVisible}",
                $"SearchBusy={viewModel.IsSearchInProgress}",
                $"FilterBusy={viewModel.IsFilterInProgress}",
                $"StatusBusy={viewModel.StatusBusy}"
            ]);
    }
}
