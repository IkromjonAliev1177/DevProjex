namespace DevProjex.Avalonia.Views;

public partial class SettingsPanelView : UserControl
{
    private const double DefaultHeaderMinimumGap = 3.0;
    private const double MinimumWidthSafetyPadding = 2.0;
    private static readonly Size InfiniteMeasureSize = new(double.PositiveInfinity, double.PositiveInfinity);

    private Border? _panelRoot;
    private Grid? _ignoreHeaderGrid;
    private TextBlock? _ignoreHeaderText;
    private CheckBox? _ignoreAllCheckBox;
    private Grid? _extensionsHeaderGrid;
    private TextBlock? _extensionsHeaderText;
    private CheckBox? _extensionsAllCheckBox;
    private Grid? _rootFoldersHeaderGrid;
    private TextBlock? _rootFoldersHeaderText;
    private CheckBox? _rootFoldersAllCheckBox;
    private double _lastReportedMinimumWidth;
    private bool _minimumWidthRefreshQueued;
    private bool _pendingForcedMinimumWidthRefresh;

    public event EventHandler<RoutedEventArgs>? ApplySettingsRequested;
    public event EventHandler<RoutedEventArgs>? IgnoreAllChanged;
    public event EventHandler<RoutedEventArgs>? ExtensionsAllChanged;
    public event EventHandler<RoutedEventArgs>? RootAllChanged;
    public event EventHandler<SettingsPanelMinimumWidthChangedEventArgs>? MinimumWidthChanged;

    public SettingsPanelView()
    {
        InitializeComponent();

        _panelRoot = this.FindControl<Border>("PanelRoot");
        _ignoreHeaderGrid = this.FindControl<Grid>("IgnoreHeaderGrid");
        _ignoreHeaderText = this.FindControl<TextBlock>("IgnoreHeaderText");
        _ignoreAllCheckBox = this.FindControl<CheckBox>("IgnoreAllCheckBox");
        _extensionsHeaderGrid = this.FindControl<Grid>("ExtensionsHeaderGrid");
        _extensionsHeaderText = this.FindControl<TextBlock>("ExtensionsHeaderText");
        _extensionsAllCheckBox = this.FindControl<CheckBox>("ExtensionsAllCheckBox");
        _rootFoldersHeaderGrid = this.FindControl<Grid>("RootFoldersHeaderGrid");
        _rootFoldersHeaderText = this.FindControl<TextBlock>("RootFoldersHeaderText");
        _rootFoldersAllCheckBox = this.FindControl<CheckBox>("RootFoldersAllCheckBox");

        SubscribeToMinimumWidthAffectingSizeChanges(_ignoreHeaderText);
        SubscribeToMinimumWidthAffectingSizeChanges(_ignoreAllCheckBox);
        SubscribeToMinimumWidthAffectingSizeChanges(_extensionsHeaderText);
        SubscribeToMinimumWidthAffectingSizeChanges(_extensionsAllCheckBox);
        SubscribeToMinimumWidthAffectingSizeChanges(_rootFoldersHeaderText);
        SubscribeToMinimumWidthAffectingSizeChanges(_rootFoldersAllCheckBox);

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnApplySettings(object? sender, RoutedEventArgs e)
        => ApplySettingsRequested?.Invoke(sender, e);

    private void OnIgnoreAllChanged(object? sender, RoutedEventArgs e)
        => IgnoreAllChanged?.Invoke(sender, e);

    private void OnExtensionsAllChanged(object? sender, RoutedEventArgs e)
        => ExtensionsAllChanged?.Invoke(sender, e);

    private void OnRootAllChanged(object? sender, RoutedEventArgs e)
        => RootAllChanged?.Invoke(sender, e);

    public void RequestMinimumWidthRefresh()
        => QueueMinimumWidthRefresh(force: true);

    public double GetRequiredMinimumWidth()
        => CalculateRequiredMinimumWidth();

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => QueueMinimumWidthRefresh(force: true);

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _minimumWidthRefreshQueued = false;
        _pendingForcedMinimumWidthRefresh = false;
    }

    private void ReportMinimumWidthIfChanged(bool force)
    {
        var minimumWidth = CalculateRequiredMinimumWidth();
        if (!force && Math.Abs(minimumWidth - _lastReportedMinimumWidth) < 0.5)
            return;

        _lastReportedMinimumWidth = minimumWidth;
        MinimumWidthChanged?.Invoke(this, new SettingsPanelMinimumWidthChangedEventArgs(minimumWidth));
    }

    private double CalculateRequiredMinimumWidth()
    {
        var contentWidth = Math.Max(
            MeasureHeaderWidth(_ignoreHeaderGrid, _ignoreHeaderText, _ignoreAllCheckBox),
            Math.Max(
                MeasureHeaderWidth(_extensionsHeaderGrid, _extensionsHeaderText, _extensionsAllCheckBox),
                MeasureHeaderWidth(_rootFoldersHeaderGrid, _rootFoldersHeaderText, _rootFoldersAllCheckBox)));

        var panelPadding = _panelRoot?.Padding ?? default;
        var borderThickness = _panelRoot?.BorderThickness ?? default;

        var totalWidth = contentWidth
                         + panelPadding.Left
                         + panelPadding.Right
                         + borderThickness.Left
                         + borderThickness.Right
                         + MinimumWidthSafetyPadding;

        return Math.Ceiling(Math.Max(240.0, totalWidth));
    }

    // Measure against infinite width so the computed minimum reflects the real content width,
    // not the current constrained layout width.
    private static double MeasureHeaderWidth(Grid? headerGrid, Control? title, CheckBox? allCheckBox)
    {
        var titleWidth = MeasureControlWidth(title);
        var checkBoxWidth = MeasureControlWidth(allCheckBox);
        if (titleWidth <= 0 || checkBoxWidth <= 0)
            return titleWidth + checkBoxWidth;

        return titleWidth + GetHeaderGap(headerGrid) + checkBoxWidth;
    }

    private static double MeasureControlWidth(Control? control)
    {
        if (control is null)
            return 0;

        control.Measure(InfiniteMeasureSize);
        return control.DesiredSize.Width + control.Margin.Left + control.Margin.Right;
    }

    private void QueueMinimumWidthRefresh(bool force)
    {
        _pendingForcedMinimumWidthRefresh |= force;
        if (_minimumWidthRefreshQueued)
            return;

        _minimumWidthRefreshQueued = true;
        Dispatcher.UIThread.Post(
            FlushPendingMinimumWidthRefresh,
            DispatcherPriority.Render);
    }

    private static double GetHeaderGap(Grid? headerGrid)
    {
        if (headerGrid?.ColumnDefinitions.Count > 1)
        {
            var gapColumn = headerGrid.ColumnDefinitions[1].Width;
            if (gapColumn.IsAbsolute)
                return gapColumn.Value;
        }

        return DefaultHeaderMinimumGap;
    }

    private void FlushPendingMinimumWidthRefresh()
    {
        _minimumWidthRefreshQueued = false;
        var force = _pendingForcedMinimumWidthRefresh;
        _pendingForcedMinimumWidthRefresh = false;
        ReportMinimumWidthIfChanged(force);
    }

    private void SubscribeToMinimumWidthAffectingSizeChanges(Control? control)
    {
        if (control is null)
            return;

        control.SizeChanged += OnMinimumWidthAffectingSizeChanged;
    }

    private void OnMinimumWidthAffectingSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 0.5
            && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 0.5)
        {
            return;
        }

        QueueMinimumWidthRefresh(force: false);
    }
}

public sealed class SettingsPanelMinimumWidthChangedEventArgs(double minimumWidth) : EventArgs
{
    public double MinimumWidth { get; } = minimumWidth;
}
