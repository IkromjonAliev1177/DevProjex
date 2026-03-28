using Avalonia.VisualTree;
using DevProjex.Application.Services;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DevProjex.Tests.UI;

internal static class UiTestDriver
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private static readonly bool FastTimingsEnabled =
        string.Equals(Environment.GetEnvironmentVariable("DEVPROJEX_FAST_UI_TESTS"), "1", StringComparison.Ordinal);
    private static readonly TimeSpan PollDelay = FastTimingsEnabled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan FrameDelay = FastTimingsEnabled ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(6);

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
        var control = window.FindControl<T>(name) ??
                      window.GetVisualDescendants()
                          .OfType<T>()
                          .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return Assert.IsType<T>(control);
    }

    public static T GetRequiredTopMenuControl<T>(MainWindow window, string name)
        where T : Control
    {
        var topMenuBar = GetRequiredControl<TopMenuBarView>(window, "TopMenuBar");
        var control = topMenuBar.FindControl<T>(name);
        return Assert.IsType<T>(control);
    }

    public static CheckBox GetRequiredIgnoreOptionCheckBox(MainWindow window, IgnoreOptionId optionId)
    {
        var checkBox = window
            .GetVisualDescendants()
            .OfType<CheckBox>()
            .FirstOrDefault(control => control.DataContext is IgnoreOptionViewModel option &&
                                       option.Id == optionId &&
                                       IsInteractableWithinWindow(control, window));

        return Assert.IsType<CheckBox>(checkBox);
    }

    public static CheckBox GetRequiredRootFolderCheckBox(MainWindow window, string rootFolderName)
    {
        var checkBox = window
            .GetVisualDescendants()
            .OfType<CheckBox>()
            .FirstOrDefault(control => control.DataContext is SelectionOptionViewModel option &&
                                       string.Equals(option.Name, rootFolderName, StringComparison.Ordinal) &&
                                       IsInteractableWithinWindow(control, window));

        return Assert.IsType<CheckBox>(checkBox);
    }

    public static CheckBox GetRequiredExtensionCheckBox(MainWindow window, string extensionName)
    {
        var checkBox = window
            .GetVisualDescendants()
            .OfType<CheckBox>()
            .FirstOrDefault(control => control.DataContext is SelectionOptionViewModel option &&
                                       string.Equals(option.Name, extensionName, StringComparison.Ordinal) &&
                                       GetViewModel(window).Extensions.Contains(option) &&
                                       IsInteractableWithinWindow(control, window));

        return Assert.IsType<CheckBox>(checkBox);
    }

    public static Button GetRequiredApplySettingsButton(MainWindow window)
    {
        var viewModel = GetViewModel(window);
        var button = window
            .GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(control => control.IsVisible &&
                                       string.Equals(control.Content?.ToString(), viewModel.SettingsApply, StringComparison.Ordinal));

        return Assert.IsType<Button>(button);
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

    public static async Task WaitForIgnoreOptionStateAsync(
        MainWindow window,
        IgnoreOptionId optionId,
        bool visible,
        bool? isChecked = null)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var option = GetViewModel(window).IgnoreOptions.FirstOrDefault(item => item.Id == optionId);
                if (!visible)
                    return option is null;

                if (option is null)
                    return false;

                return isChecked is null || option.IsChecked == isChecked.Value;
            },
            $"ignore option {optionId} to become visible={visible} checked={isChecked?.ToString() ?? "<any>"}");

        await WaitForSettledFramesAsync(frameCount: 8);
    }

    public static async Task WaitForIgnoreOptionLabelAsync(
        MainWindow window,
        IgnoreOptionId optionId,
        string expectedLabel)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var option = GetViewModel(window).IgnoreOptions.FirstOrDefault(item => item.Id == optionId);
                return option is not null &&
                       string.Equals(option.Label, expectedLabel, StringComparison.Ordinal);
            },
            $"ignore option {optionId} label to become '{expectedLabel}'");

        await WaitForSettledFramesAsync(frameCount: 8);
    }

    public static async Task WaitForStatusMetricsAsync(
        MainWindow window,
        ExportOutputMetrics expectedTreeMetrics,
        ExportOutputMetrics expectedContentMetrics)
    {
        await WaitForConditionAsync(
            window,
            () =>
            {
                var viewModel = GetViewModel(window);
                if (!viewModel.StatusMetricsVisible ||
                    string.IsNullOrWhiteSpace(viewModel.StatusTreeStatsText) ||
                    string.IsNullOrWhiteSpace(viewModel.StatusContentStatsText))
                {
                    return false;
                }

                return TryParseStatusMetrics(viewModel.StatusTreeStatsText, out var actualTreeMetrics) &&
                       TryParseStatusMetrics(viewModel.StatusContentStatsText, out var actualContentMetrics) &&
                       actualTreeMetrics == expectedTreeMetrics &&
                       actualContentMetrics == expectedContentMetrics;
            },
            "status metrics to match the expected applied export snapshot",
            timeout: TimeSpan.FromSeconds(30));

        await WaitForSettledFramesAsync(frameCount: 12);
    }

    public static bool TryParseStatusMetrics(string text, out ExportOutputMetrics metrics)
    {
        metrics = ExportOutputMetrics.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalizedText = text.Replace('\u00A0', ' ');
        var segments = normalizedText.Trim('[', ']').Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
            return false;

        var tokens = new string[3];
        for (var index = 0; index < 3; index++)
        {
            var match = Regex.Match(segments[index], @"([0-9][0-9 ]*(?:\.[0-9])?[KM]?)");
            if (!match.Success)
                return false;

            tokens[index] = match.Groups[1].Value;
        }

        if (!TryParseMetricNumber(tokens[0], out var lines) ||
            !TryParseMetricNumber(tokens[1], out var chars) ||
            !TryParseMetricNumber(tokens[2], out var tokenCount))
        {
            return false;
        }

        metrics = new ExportOutputMetrics(lines, chars, tokenCount);
        return true;
    }

    public static IReadOnlyCollection<IgnoreOptionId> GetSelectedIgnoreOptionIds(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_selectionCoordinator", BindingFlags.Instance | BindingFlags.NonPublic);
        var coordinator = Assert.IsType<DevProjex.Avalonia.Coordinators.SelectionSyncCoordinator>(field?.GetValue(window));
        return coordinator.GetSelectedIgnoreOptionIds();
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
        var stickyHeaderText = GetRequiredControl<TextBlock>(window, "PreviewStickyHeaderText");
        var scrollViewer = GetRequiredPreviewScrollViewer(window);
        var previewTextControl = GetRequiredControl<DevProjex.Avalonia.Controls.VirtualizedPreviewTextControl>(window, "PreviewTextControl");

        var firstSectionStartLine = await WaitForPreviewScrollRangeAsync(window, scrollViewer);

        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var step = ResolvePreviewScrollStep(scrollViewer);
        var reachedTopLine = previewTextControl.GetLineNumberAtVerticalOffset(scrollViewer.Offset.Y);
        var reachedOffset = scrollViewer.Offset.Y;

        for (var offset = step; offset <= maxOffset + step; offset += step)
        {
            reachedOffset = Math.Min(maxOffset, offset);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, reachedOffset);

            // Linux headless can lag one render behind when the ScrollViewer offset is
            // applied to the virtualized preview control. We wait for the propagated
            // VerticalOffset instead of waiting for the sticky threshold itself, because
            // the first section may require several scroll steps before it becomes reachable.
            await WaitForConditionAsync(
                window,
                () => stickyHeaderContainer.IsVisible ||
                      Math.Abs(previewTextControl.VerticalOffset - reachedOffset) <= 0.5,
                "preview scroll position to propagate into the virtualized preview control",
                timeout: TimeSpan.FromSeconds(2));

            await WaitForSettledFramesAsync(frameCount: 3);
            reachedTopLine = previewTextControl.GetLineNumberAtVerticalOffset(previewTextControl.VerticalOffset);
            if (stickyHeaderContainer.IsVisible && !string.IsNullOrWhiteSpace(stickyHeaderText.Text))
                return;

            if (reachedTopLine >= firstSectionStartLine)
                break;
        }

        await WaitForConditionAsync(
            window,
            () => stickyHeaderContainer.IsVisible && !string.IsNullOrWhiteSpace(stickyHeaderText.Text),
            "sticky preview path to become visible after the viewport reaches the first file section",
            timeout: TimeSpan.FromSeconds(5));

        if (stickyHeaderContainer.IsVisible && !string.IsNullOrWhiteSpace(stickyHeaderText.Text))
            return;

        throw new XunitException(
            "Sticky preview path never became visible after scrolling through the preview content. " +
            $"firstSectionStartLine={firstSectionStartLine}, reachedTopLine={reachedTopLine}, " +
            $"offset={reachedOffset:0.##}, controlOffset={previewTextControl.VerticalOffset:0.##}, maxOffset={maxOffset:0.##}, " +
            $"extent={scrollViewer.Extent.Height:0.##}, viewport={scrollViewer.Viewport.Height:0.##}");
    }

    public static async Task ScrollPreviewUntilStickyHeaderTextChangesAsync(MainWindow window, string previousText)
    {
        var stickyHeaderText = GetRequiredControl<TextBlock>(window, "PreviewStickyHeaderText");
        var scrollViewer = GetRequiredPreviewScrollViewer(window);
        var previewTextControl = GetRequiredControl<DevProjex.Avalonia.Controls.VirtualizedPreviewTextControl>(window, "PreviewTextControl");
        var firstSectionStartLine = await WaitForPreviewScrollRangeAsync(window, scrollViewer);
        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var step = ResolvePreviewScrollStep(scrollViewer);

        for (var offset = scrollViewer.Offset.Y + step; offset <= maxOffset + step; offset += step)
        {
            var targetOffset = Math.Min(maxOffset, offset);
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
            await WaitForConditionAsync(
                window,
                () => Math.Abs(previewTextControl.VerticalOffset - targetOffset) <= 0.5,
                "preview scroll position to propagate into the virtualized preview control",
                timeout: TimeSpan.FromSeconds(2));
            await WaitForSettledFramesAsync(frameCount: 3);

            if (previewTextControl.GetLineNumberAtVerticalOffset(previewTextControl.VerticalOffset) < firstSectionStartLine)
                continue;

            if (!string.IsNullOrWhiteSpace(stickyHeaderText.Text) &&
                !string.Equals(stickyHeaderText.Text, previousText, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new XunitException("Sticky preview path text never changed after scrolling through multiple preview sections.");
    }

    private static async Task<int> WaitForPreviewScrollRangeAsync(MainWindow window, ScrollViewer scrollViewer)
    {
        var firstSectionStartLine = 1;

        // The sticky header depends on the virtualized preview document sections and on
        // the ScrollViewer extent being fully measured. If we start scrolling before both
        // are ready, CI can observe a zero or undersized scroll range and falsely report
        // that the sticky header never appears.
        await WaitForConditionAsync(
            window,
            () =>
            {
                var document = GetViewModel(window).PreviewDocument;
                if (document?.Sections is not { Count: > 0 })
                    return false;

                firstSectionStartLine = document.Sections[0].StartLine;
                return scrollViewer.Viewport.Height > 0 &&
                       scrollViewer.Extent.Height > scrollViewer.Viewport.Height;
            },
            "preview document sections and scroll range to become measurable");

        return firstSectionStartLine;
    }

    private static double ResolvePreviewScrollStep(ScrollViewer scrollViewer)
        => Math.Max(48, Math.Min(144, scrollViewer.Viewport.Height / 4));

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

            await Task.Delay(PollDelay);
        }

        throw new XunitException($"Timed out waiting for {description}. Current state: {DescribeState(window)}");
    }

    public static async Task WaitForSettledFramesAsync(int frameCount)
    {
        var effectiveFrameCount = FastTimingsEnabled
            ? Math.Max(1, (int)Math.Ceiling(frameCount * 0.35))
            : frameCount;

        for (var index = 0; index < effectiveFrameCount; index++)
        {
            // Drive both dispatcher queues and the headless render timer so tests observe
            // the same visual state users would see after an animation or layout pass.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            await Task.Delay(FrameDelay);
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
                $"StatusBusy={viewModel.StatusBusy}",
                $"StatusTree={viewModel.StatusTreeStatsText}",
                $"StatusContent={viewModel.StatusContentStatsText}"
            ]);
    }

    private static bool IsInteractableWithinWindow(Control control, MainWindow window)
        => control.IsVisible && control.TranslatePoint(default, window).HasValue;

    private static bool TryParseMetricNumber(string text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var multiplier = 1.0;
        if (normalized.EndsWith('K'))
        {
            multiplier = 1_000.0;
            normalized = normalized[..^1];
        }
        else if (normalized.EndsWith('M'))
        {
            multiplier = 1_000_000.0;
            normalized = normalized[..^1];
        }

        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;

        value = (int)Math.Round(parsed * multiplier, MidpointRounding.AwayFromZero);
        return true;
    }
}
