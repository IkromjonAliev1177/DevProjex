using Avalonia.Media.TextFormatting;

namespace DevProjex.Avalonia.Controls;

/// <summary>
/// Draws only visible preview text lines for large payloads.
/// Rendering stays virtualized while the underlying document can be either in-memory
/// or file-backed, which keeps preview RAM bounded for huge repositories.
/// </summary>
public sealed class VirtualizedPreviewTextControl : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<IPreviewTextDocument?> DocumentProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, IPreviewTextDocument?>(nameof(Document));

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(VerticalOffset));

    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(ViewportHeight));

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(ViewportWidth));

    public static readonly StyledProperty<double> TopPaddingProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(TopPadding), 10.0);

    public static readonly StyledProperty<double> BottomPaddingProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(BottomPadding), 10.0);

    public static readonly StyledProperty<double> LeftPaddingProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(LeftPadding), 10.0);

    public static readonly StyledProperty<double> RightPaddingProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(RightPadding), 16.0);

    public static readonly StyledProperty<FontFamily?> TextFontFamilyProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, FontFamily?>(
            nameof(TextFontFamily),
            FontFamily.Default);

    public static readonly StyledProperty<double> TextFontSizeProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(TextFontSize), 15.0);

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<string> CopyMenuHeaderProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, string>(nameof(CopyMenuHeader), "Copy");

    public static readonly StyledProperty<string> SelectAllMenuHeaderProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, string>(nameof(SelectAllMenuHeader), "Select All");

    public static readonly StyledProperty<string> ClearSelectionMenuHeaderProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, string>(nameof(ClearSelectionMenuHeader), "Clear selection");

    private const int RenderBufferLines = 3;
    private const double AutoScrollEdgeThreshold = 28.0;
    private static readonly TimeSpan AutoScrollTickInterval = TimeSpan.FromMilliseconds(16);
    private readonly List<int> _lineStarts = [0];
    private DispatcherTimer? _selectionAutoScrollTimer;
    private VisibleTextWindow? _cachedVisibleWindow;
    private IPreviewTextDocument? _cachedVisibleWindowDocument;
    private int _cachedVisibleWindowFirstLine;
    private int _cachedVisibleWindowLastLine;
    private ScrollViewer? _ownerScrollViewer;
    private int _lineCount = 1;
    private int _maxLineLength;
    private SelectionPosition? _selectionAnchor;
    private SelectionPosition? _selectionActive;
    private bool _isSelecting;
    private Point _selectionPointerViewportPoint;
    private ThemeVariant? _cachedSelectionTheme;
    private IBrush? _cachedSelectionBackground;
    private IBrush? _cachedSelectionForeground;
    private ContextMenu? _contextMenu;
    private MenuItem? _copyMenuItem;
    private MenuItem? _selectAllMenuItem;
    private MenuItem? _clearSelectionMenuItem;
    private static readonly global::Avalonia.Input.Cursor PreviewTextCursor =
        new(global::Avalonia.Input.StandardCursorType.Ibeam);

    static VirtualizedPreviewTextControl()
    {
        AffectsRender<VirtualizedPreviewTextControl>(
            TextProperty,
            DocumentProperty,
            VerticalOffsetProperty,
            ViewportHeightProperty,
            ViewportWidthProperty,
            TopPaddingProperty,
            BottomPaddingProperty,
            LeftPaddingProperty,
            RightPaddingProperty,
            TextFontFamilyProperty,
            TextFontSizeProperty,
            TextBrushProperty,
            CopyMenuHeaderProperty,
            SelectAllMenuHeaderProperty,
            ClearSelectionMenuHeaderProperty);

        AffectsMeasure<VirtualizedPreviewTextControl>(
            TextProperty,
            DocumentProperty,
            ViewportWidthProperty,
            TopPaddingProperty,
            BottomPaddingProperty,
            LeftPaddingProperty,
            RightPaddingProperty,
            TextFontFamilyProperty,
            TextFontSizeProperty);

        TextProperty.Changed.AddClassHandler<VirtualizedPreviewTextControl>((control, _) =>
        {
            control.RebuildTextLayoutMetadata();
        });

        DocumentProperty.Changed.AddClassHandler<VirtualizedPreviewTextControl>((control, _) =>
        {
            control.RebuildTextLayoutMetadata();
        });

        CopyMenuHeaderProperty.Changed.AddClassHandler<VirtualizedPreviewTextControl>((control, _) =>
        {
            control.UpdateContextMenuHeaders();
        });

        SelectAllMenuHeaderProperty.Changed.AddClassHandler<VirtualizedPreviewTextControl>((control, _) =>
        {
            control.UpdateContextMenuHeaders();
        });

        ClearSelectionMenuHeaderProperty.Changed.AddClassHandler<VirtualizedPreviewTextControl>((control, _) =>
        {
            control.UpdateContextMenuHeaders();
        });
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IPreviewTextDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double VerticalOffset
    {
        get => GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public double ViewportHeight
    {
        get => GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    public double ViewportWidth
    {
        get => GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    public double TopPadding
    {
        get => GetValue(TopPaddingProperty);
        set => SetValue(TopPaddingProperty, value);
    }

    public double BottomPadding
    {
        get => GetValue(BottomPaddingProperty);
        set => SetValue(BottomPaddingProperty, value);
    }

    public double LeftPadding
    {
        get => GetValue(LeftPaddingProperty);
        set => SetValue(LeftPaddingProperty, value);
    }

    public double RightPadding
    {
        get => GetValue(RightPaddingProperty);
        set => SetValue(RightPaddingProperty, value);
    }

    public FontFamily? TextFontFamily
    {
        get => GetValue(TextFontFamilyProperty);
        set => SetValue(TextFontFamilyProperty, value);
    }

    public double TextFontSize
    {
        get => GetValue(TextFontSizeProperty);
        set => SetValue(TextFontSizeProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public string CopyMenuHeader
    {
        get => GetValue(CopyMenuHeaderProperty);
        set => SetValue(CopyMenuHeaderProperty, value);
    }

    public string SelectAllMenuHeader
    {
        get => GetValue(SelectAllMenuHeaderProperty);
        set => SetValue(SelectAllMenuHeaderProperty, value);
    }

    public string ClearSelectionMenuHeader
    {
        get => GetValue(ClearSelectionMenuHeaderProperty);
        set => SetValue(ClearSelectionMenuHeaderProperty, value);
    }

    public bool HasSelection => TryGetNormalizedSelection(out _, out _);

    public VirtualizedPreviewTextControl()
    {
        Focusable = true;
        Cursor = PreviewTextCursor;
        RebuildTextLayoutMetadata();
    }

    public void ClearSelection()
    {
        _isSelecting = false;
        StopSelectionAutoScroll();
        ClearSelectionState(invalidateVisual: true);
    }

    public bool TryHandleViewportSelectionStart(IPointer pointer, Point viewportPoint, KeyModifiers keyModifiers)
    {
        Focus();

        var scrollViewer = GetOwnerScrollViewer();
        var documentPoint = scrollViewer is not null
            ? new Point(scrollViewer.Offset.X + viewportPoint.X, scrollViewer.Offset.Y + viewportPoint.Y)
            : viewportPoint;

        _selectionPointerViewportPoint = viewportPoint;
        return TryStartSelection(pointer, documentPoint, keyModifiers);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var typeface = ResolveTypeface();
        var lineHeight = ResolveLineHeight(typeface);
        var width = Math.Max(CalculateRequiredWidth(typeface), Math.Ceiling(Math.Max(0, ViewportWidth)));
        var height = Math.Ceiling(TopPadding + BottomPadding + (ResolveLineCount() * lineHeight));

        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var lineCount = ResolveLineCount();
        if (lineCount <= 0)
            return;

        var typeface = ResolveTypeface();
        var lineHeight = ResolveLineHeight(typeface);
        if (lineHeight <= 0)
            return;

        var viewportTop = Math.Max(0, VerticalOffset);
        var viewportHeight = ViewportHeight > 0 ? ViewportHeight : Bounds.Height;
        var firstVisibleLine = Math.Max(1, (int)Math.Floor((viewportTop - TopPadding) / lineHeight) + 1);
        var visibleLineCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / lineHeight));
        var lastVisibleLine = Math.Min(lineCount, firstVisibleLine + visibleLineCount - 1);

        firstVisibleLine = Math.Max(1, firstVisibleLine - RenderBufferLines);
        lastVisibleLine = Math.Min(lineCount, lastVisibleLine + RenderBufferLines);
        if (lastVisibleLine < firstVisibleLine)
            return;

        var visibleWindow = BuildVisibleTextWindow(firstVisibleLine, lastVisibleLine);
        if (visibleWindow.Text.Length == 0)
            return;

        var origin = new Point(LeftPadding, TopPadding + (firstVisibleLine - 1) * lineHeight);
        var formattedText = BuildFormattedText(visibleWindow.Text, typeface);

        if (TryGetVisibleSelectionRange(visibleWindow, out var selectionStart, out var selectionLength))
        {
            var (selectionBackground, selectionForeground) = ResolveSelectionBrushes();
            if (selectionForeground is not null)
                formattedText.SetForegroundBrush(selectionForeground, selectionStart, selectionLength);

            var selectionGeometry = formattedText.BuildHighlightGeometry(origin, selectionStart, selectionLength);
            if (selectionGeometry is not null)
                context.DrawGeometry(selectionBackground, null, selectionGeometry);
        }

        context.DrawText(formattedText, origin);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            Focus();
            OpenContextMenu();
            e.Handled = true;
            return;
        }

        if (!properties.IsLeftButtonPressed)
            return;

        Focus();
        CaptureSelectionPointer(e);
        TryStartSelection(e.Pointer, e.GetPosition(this), e.KeyModifiers);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isSelecting || e.Pointer.Captured != this)
            return;

        CaptureSelectionPointer(e);
        UpdateSelectionActivePosition(HitTestSelectionPosition(e.GetPosition(this)));
        UpdateSelectionAutoScrollState();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isSelecting)
            return;

        CaptureSelectionPointer(e);
        UpdateSelectionActivePosition(HitTestSelectionPosition(e.GetPosition(this)));
        EndSelectionCapture(e.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isSelecting = false;
        StopSelectionAutoScroll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
            return;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C && HasSelection)
        {
            _ = CopySelectionToClipboardAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && (_selectionAnchor is not null || _selectionActive is not null))
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isSelecting = false;
        StopSelectionAutoScroll();
        _ownerScrollViewer = null;
        CloseContextMenu();
    }

    private void RebuildTextLayoutMetadata()
    {
        ResetVisibleWindowCache();
        ClearSelectionState(invalidateVisual: false);
        StopSelectionAutoScroll();
        _cachedSelectionTheme = null;
        _cachedSelectionBackground = null;
        _cachedSelectionForeground = null;

        if (Document is { } document)
        {
            _lineStarts.Clear();
            _lineStarts.Add(0);
            _lineCount = Math.Max(1, document.LineCount);
            _maxLineLength = Math.Max(0, document.MaxLineLength);
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        RebuildStringMetadata();
    }

    private void RebuildStringMetadata()
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);
        _lineCount = 1;
        _maxLineLength = 0;

        var text = Text ?? string.Empty;
        if (text.Length == 0)
        {
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        var currentLineLength = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\n')
            {
                if (currentLineLength > _maxLineLength)
                    _maxLineLength = currentLineLength;

                currentLineLength = 0;
                _lineStarts.Add(i + 1);
                continue;
            }

            if (ch != '\r')
                currentLineLength++;
        }

        if (currentLineLength > _maxLineLength)
            _maxLineLength = currentLineLength;

        _lineCount = Math.Max(1, _lineStarts.Count);
        InvalidateMeasure();
        InvalidateVisual();
    }

    private VisibleTextWindow BuildVisibleTextWindow(int firstVisibleLine, int lastVisibleLine)
    {
        if (_cachedVisibleWindow is not null &&
            ReferenceEquals(_cachedVisibleWindowDocument, Document) &&
            _cachedVisibleWindowFirstLine == firstVisibleLine &&
            _cachedVisibleWindowLastLine == lastVisibleLine)
        {
            return _cachedVisibleWindow;
        }

        var text = BuildVisibleLinesText(firstVisibleLine, lastVisibleLine);
        _cachedVisibleWindow = new VisibleTextWindow(firstVisibleLine, lastVisibleLine, text);
        _cachedVisibleWindowDocument = Document;
        _cachedVisibleWindowFirstLine = firstVisibleLine;
        _cachedVisibleWindowLastLine = lastVisibleLine;
        return _cachedVisibleWindow;
    }

    private string BuildVisibleLinesText(int firstVisibleLine, int lastVisibleLine)
    {
        if (Document is { } document)
            return document.GetLineRangeText(firstVisibleLine, lastVisibleLine);

        var text = Text ?? string.Empty;
        if (text.Length == 0)
            return string.Empty;

        var linesCount = Math.Max(0, lastVisibleLine - firstVisibleLine + 1);
        var estimatedLineLength = Math.Max(12, Math.Min(_maxLineLength, 256));
        var builder = new StringBuilder(linesCount * (estimatedLineLength + 1));

        for (var lineIndex = firstVisibleLine - 1; lineIndex <= lastVisibleLine - 1; lineIndex++)
        {
            var line = GetStringLineSpan(lineIndex);
            if (!line.IsEmpty)
                builder.Append(line);

            if (lineIndex < lastVisibleLine - 1)
                builder.Append('\n');
        }

        return builder.ToString();
    }

    private bool TryGetVisibleSelectionRange(VisibleTextWindow visibleWindow, out int selectionStart, out int selectionLength)
    {
        selectionStart = 0;
        selectionLength = 0;

        if (!TryGetNormalizedSelection(out var selectionRangeStart, out var selectionRangeEnd))
            return false;

        var windowStart = new SelectionPosition(visibleWindow.FirstLine, 0);
        var windowEnd = new SelectionPosition(visibleWindow.LastLine, visibleWindow.GetLineLength(visibleWindow.LastLine));

        if (ComparePositions(selectionRangeEnd, windowStart) <= 0 ||
            ComparePositions(selectionRangeStart, windowEnd) >= 0)
        {
            return false;
        }

        var clampedStart = ComparePositions(selectionRangeStart, windowStart) < 0
            ? windowStart
            : visibleWindow.Clamp(selectionRangeStart);
        var clampedEnd = ComparePositions(selectionRangeEnd, windowEnd) > 0
            ? windowEnd
            : visibleWindow.Clamp(selectionRangeEnd);

        var localStart = visibleWindow.GetLocalTextIndex(clampedStart.Line, clampedStart.Column);
        var localEnd = visibleWindow.GetLocalTextIndex(clampedEnd.Line, clampedEnd.Column);
        if (localEnd <= localStart)
            return false;

        selectionStart = localStart;
        selectionLength = localEnd - localStart;
        return true;
    }

    private (IBrush SelectionBackground, IBrush? SelectionForeground) ResolveSelectionBrushes()
    {
        var app = global::Avalonia.Application.Current;
        var theme = app?.ActualThemeVariant ?? ThemeVariant.Light;

        if (_cachedSelectionTheme == theme && _cachedSelectionBackground is not null)
            return (_cachedSelectionBackground, _cachedSelectionForeground);

        _cachedSelectionTheme = theme;
        _cachedSelectionBackground = theme == ThemeVariant.Dark
            ? new SolidColorBrush(Color.Parse("#254861"))
            : new SolidColorBrush(Color.Parse("#DCEEFF"));
        _cachedSelectionForeground = TextBrush ?? Brushes.White;

        if (app?.Resources.TryGetResource("TreeSelectionBrush", theme, out var selectionBackground) == true &&
            selectionBackground is IBrush selectionBackgroundBrush)
        {
            _cachedSelectionBackground = selectionBackgroundBrush;
        }

        if (app?.Resources.TryGetResource("TreeSelectionTextBrush", theme, out var selectionForeground) == true &&
            selectionForeground is IBrush selectionForegroundBrush)
        {
            _cachedSelectionForeground = selectionForegroundBrush;
        }

        return (_cachedSelectionBackground, _cachedSelectionForeground);
    }

    private SelectionPosition HitTestSelectionPosition(Point point)
        => HitTestSelection(point).Position;

    private SelectionHitResult HitTestSelection(Point point)
    {
        var typeface = ResolveTypeface();
        var lineHeight = ResolveLineHeight(typeface);
        if (lineHeight <= 0)
            return new SelectionHitResult(new SelectionPosition(1, 0), SelectionHitKind.Empty);

        var lineCount = ResolveLineCount();
        var relativeY = point.Y - TopPadding;
        if (relativeY < 0)
            return new SelectionHitResult(new SelectionPosition(1, 0), SelectionHitKind.Empty);

        var rawLineNumber = (int)Math.Floor(relativeY / lineHeight) + 1;
        if (rawLineNumber > lineCount)
        {
            var lastLine = Math.Max(1, lineCount);
            return new SelectionHitResult(
                new SelectionPosition(lastLine, GetLineText(lastLine).Length),
                SelectionHitKind.Empty);
        }

        var lineNumber = Math.Clamp(rawLineNumber, 1, lineCount);

        var x = Math.Max(0, point.X - LeftPadding);
        var lineText = GetLineText(lineNumber);
        var column = ResolveColumnFromDistance(lineText, x, typeface);
        return new SelectionHitResult(
            new SelectionPosition(lineNumber, column),
            SelectionHitKind.Text);
    }

    private int ResolveColumnFromDistance(string lineText, double distance, Typeface typeface)
    {
        if (string.IsNullOrEmpty(lineText) || distance <= 0)
            return 0;

        var layout = new TextLayout(
            lineText,
            typeface,
            TextFontSize,
            TextBrush ?? Brushes.White);
        if (layout.TextLines.Count == 0)
            return 0;

        var characterHit = layout.TextLines[0].GetCharacterHitFromDistance(distance);
        var column = characterHit.FirstCharacterIndex + Math.Max(0, characterHit.TrailingLength);
        return Math.Clamp(column, 0, lineText.Length);
    }

    private void UpdateSelectionActivePosition(SelectionPosition position)
    {
        if (_selectionActive == position)
            return;

        _selectionActive = position;
        InvalidateVisual();
    }

    private void ClearSelectionState(bool invalidateVisual)
    {
        var hadState = _selectionAnchor is not null || _selectionActive is not null;
        _selectionAnchor = null;
        _selectionActive = null;

        if (invalidateVisual && hadState)
            InvalidateVisual();
    }

    private bool TryGetNormalizedSelection(out SelectionPosition start, out SelectionPosition end)
    {
        if (_selectionAnchor is not { } anchor || _selectionActive is not { } active)
        {
            start = default;
            end = default;
            return false;
        }

        if (anchor == active)
        {
            start = default;
            end = default;
            return false;
        }

        if (ComparePositions(anchor, active) <= 0)
        {
            start = anchor;
            end = active;
        }
        else
        {
            start = active;
            end = anchor;
        }

        return true;
    }

    private static int ComparePositions(SelectionPosition left, SelectionPosition right)
    {
        var lineComparison = left.Line.CompareTo(right.Line);
        return lineComparison != 0
            ? lineComparison
            : left.Column.CompareTo(right.Column);
    }

    private async Task CopySelectionToClipboardAsync()
    {
        var selectedText = BuildSelectedText();
        if (string.IsNullOrEmpty(selectedText))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(selectedText);
    }

    public void SelectAll()
    {
        var lineCount = ResolveLineCount();
        if (lineCount <= 0)
        {
            ClearSelection();
            return;
        }

        var lastLine = Math.Max(1, lineCount);
        var lastLineLength = GetLineText(lastLine).Length;
        if (lastLine == 1 && lastLineLength == 0)
        {
            ClearSelection();
            return;
        }

        _selectionAnchor = new SelectionPosition(1, 0);
        _selectionActive = new SelectionPosition(lastLine, lastLineLength);
        InvalidateVisual();
    }

    private string BuildSelectedText()
    {
        if (!TryGetNormalizedSelection(out var start, out var end))
            return string.Empty;

        if (start.Line == end.Line)
        {
            var lineText = GetLineText(start.Line);
            var startColumn = Math.Clamp(start.Column, 0, lineText.Length);
            var endColumn = Math.Clamp(end.Column, startColumn, lineText.Length);
            return endColumn > startColumn
                ? lineText[startColumn..endColumn]
                : string.Empty;
        }

        var estimatedLineLength = Math.Max(12, Math.Min(Document?.MaxLineLength ?? _maxLineLength, 256));
        var builder = new StringBuilder((end.Line - start.Line + 1) * (estimatedLineLength + 1));

        for (var lineNumber = start.Line; lineNumber <= end.Line; lineNumber++)
        {
            var lineText = GetLineText(lineNumber);
            var segmentStart = lineNumber == start.Line
                ? Math.Clamp(start.Column, 0, lineText.Length)
                : 0;
            var segmentEnd = lineNumber == end.Line
                ? Math.Clamp(end.Column, segmentStart, lineText.Length)
                : lineText.Length;

            if (segmentEnd > segmentStart)
                builder.Append(lineText.AsSpan(segmentStart, segmentEnd - segmentStart));

            if (lineNumber < end.Line)
                builder.Append('\n');
        }

        return builder.ToString();
    }

    private string GetLineText(int lineNumber)
    {
        if (Document is { } document)
            return document.GetLineText(lineNumber);

        var normalizedIndex = Math.Clamp(lineNumber, 1, _lineCount) - 1;
        return GetStringLineSpan(normalizedIndex).ToString();
    }

    private ReadOnlySpan<char> GetStringLineSpan(int lineIndex)
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0 || lineIndex < 0 || lineIndex >= _lineStarts.Count)
            return ReadOnlySpan<char>.Empty;

        var start = _lineStarts[lineIndex];
        var nextStart = lineIndex + 1 < _lineStarts.Count
            ? _lineStarts[lineIndex + 1]
            : text.Length;

        var endExclusive = lineIndex + 1 < _lineStarts.Count
            ? Math.Max(start, nextStart - 1)
            : nextStart;

        if (endExclusive > start && text[endExclusive - 1] == '\r')
            endExclusive--;

        return endExclusive > start
            ? text.AsSpan(start, endExclusive - start)
            : ReadOnlySpan<char>.Empty;
    }

    private int ResolveLineCount() => Document?.LineCount ?? _lineCount;

    private Typeface ResolveTypeface() =>
        new(TextFontFamily ?? FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    private double CalculateRequiredWidth(Typeface typeface)
    {
        var sample = BuildFormattedText("W", typeface);
        var glyphWidth = Math.Max(1.0, sample.Width);
        var contentWidth = (Document?.MaxLineLength ?? _maxLineLength) * glyphWidth;
        return Math.Ceiling(LeftPadding + contentWidth + RightPadding);
    }

    private double ResolveLineHeight(Typeface typeface)
    {
        var sample = BuildFormattedText("8", typeface);
        return Math.Max(1.0, sample.Height);
    }

    private FormattedText BuildFormattedText(string text, Typeface typeface)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            TextFontSize,
            TextBrush ?? Brushes.White);
    }

    private void EnsureContextMenu()
    {
        if (_contextMenu is not null)
            return;

        _copyMenuItem = new MenuItem();
        _copyMenuItem.Click += OnCopyMenuItemClick;

        _selectAllMenuItem = new MenuItem();
        _selectAllMenuItem.Click += OnSelectAllMenuItemClick;

        _clearSelectionMenuItem = new MenuItem();
        _clearSelectionMenuItem.Click += OnClearSelectionMenuItemClick;

        _contextMenu = new ContextMenu();
        _contextMenu.Items.Add(_copyMenuItem);
        _contextMenu.Items.Add(_selectAllMenuItem);
        _contextMenu.Items.Add(new Separator());
        _contextMenu.Items.Add(_clearSelectionMenuItem);
        _contextMenu.Opening += OnContextMenuOpening;
        _contextMenu.Opened += OnContextMenuOpened;

        UpdateContextMenuHeaders();
    }

    private void UpdateContextMenuHeaders()
    {
        if (_copyMenuItem is not null)
            _copyMenuItem.Header = CopyMenuHeader;

        if (_selectAllMenuItem is not null)
            _selectAllMenuItem.Header = SelectAllMenuHeader;

        if (_clearSelectionMenuItem is not null)
            _clearSelectionMenuItem.Header = ClearSelectionMenuHeader;
    }

    private void OpenContextMenu()
    {
        EnsureContextMenu();
        _contextMenu?.Open(this);
    }

    private void CloseContextMenu()
    {
        _contextMenu?.Close();
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_copyMenuItem is not null)
            _copyMenuItem.IsEnabled = HasSelection;

        if (_selectAllMenuItem is not null)
            _selectAllMenuItem.IsEnabled = ResolveLineCount() > 0 && (ResolveLineCount() > 1 || GetLineText(1).Length > 0);

        if (_clearSelectionMenuItem is not null)
            _clearSelectionMenuItem.IsEnabled = HasSelection;
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        ApplyContextMenuBackdrop();
    }

    private void OnCopyMenuItemClick(object? sender, RoutedEventArgs e)
    {
        _ = CopySelectionToClipboardAsync();
    }

    private void OnSelectAllMenuItemClick(object? sender, RoutedEventArgs e)
    {
        SelectAll();
    }

    private void OnClearSelectionMenuItemClick(object? sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void ApplyContextMenuBackdrop()
    {
        if (_contextMenu?.GetVisualRoot() is not TopLevel popupLevel)
            return;

        var host = TopLevel.GetTopLevel(this);
        if (host is not null && ReferenceEquals(host, popupLevel))
            return;

        if (DataContext is not MainWindowViewModel viewModel)
            return;

        try
        {
            if (viewModel.HasAnyEffect)
            {
                popupLevel.TransparencyLevelHint =
                [
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.Blur,
                    WindowTransparencyLevel.Transparent,
                    WindowTransparencyLevel.None
                ];

                popupLevel.Background = Brushes.Transparent;
            }
            else
            {
                popupLevel.TransparencyLevelHint =
                [
                    WindowTransparencyLevel.None
                ];
            }
        }
        catch
        {
            // Ignore: popup can close while the host is being configured.
        }
    }

    private void ResetVisibleWindowCache()
    {
        _cachedVisibleWindow = null;
        _cachedVisibleWindowDocument = null;
        _cachedVisibleWindowFirstLine = 0;
        _cachedVisibleWindowLastLine = 0;
    }

    private ScrollViewer? GetOwnerScrollViewer()
    {
        if (_ownerScrollViewer is not null)
            return _ownerScrollViewer;

        foreach (var visual in this.GetVisualAncestors())
        {
            if (visual is not ScrollViewer scrollViewer)
                continue;

            _ownerScrollViewer = scrollViewer;
            break;
        }

        return _ownerScrollViewer;
    }

    private void CaptureSelectionPointer(PointerEventArgs e)
    {
        var scrollViewer = GetOwnerScrollViewer();
        _selectionPointerViewportPoint = scrollViewer is not null
            ? e.GetPosition(scrollViewer)
            : e.GetPosition(this);
    }

    private bool TryStartSelection(IPointer pointer, Point documentPoint, KeyModifiers keyModifiers)
    {
        var hit = HitTestSelection(documentPoint);
        if (!keyModifiers.HasFlag(KeyModifiers.Shift) && hit.Kind == SelectionHitKind.Empty)
        {
            if (HasSelection)
                ClearSelection();

            return false;
        }

        var selectionPosition = hit.Position;
        if (!keyModifiers.HasFlag(KeyModifiers.Shift) || _selectionAnchor is null)
            _selectionAnchor = selectionPosition;

        UpdateSelectionActivePosition(selectionPosition);
        _isSelecting = true;
        pointer.Capture(this);
        UpdateSelectionAutoScrollState();
        return true;
    }

    private void UpdateSelectionAutoScrollState()
    {
        if (!_isSelecting)
        {
            StopSelectionAutoScroll();
            return;
        }

        var scrollViewer = GetOwnerScrollViewer();
        if (scrollViewer is null || scrollViewer.Extent.Height <= scrollViewer.Viewport.Height)
        {
            StopSelectionAutoScroll();
            return;
        }

        var viewportHeight = scrollViewer.Viewport.Height;
        if (viewportHeight <= 0)
        {
            StopSelectionAutoScroll();
            return;
        }

        var pointerY = _selectionPointerViewportPoint.Y;
        var shouldScrollUp = pointerY < AutoScrollEdgeThreshold;
        var shouldScrollDown = pointerY > viewportHeight - AutoScrollEdgeThreshold;
        if (!shouldScrollUp && !shouldScrollDown)
        {
            StopSelectionAutoScroll();
            return;
        }

        if (_selectionAutoScrollTimer is null)
        {
            _selectionAutoScrollTimer = new DispatcherTimer
            {
                Interval = AutoScrollTickInterval
            };
            _selectionAutoScrollTimer.Tick += OnSelectionAutoScrollTick;
        }

        if (!_selectionAutoScrollTimer.IsEnabled)
            _selectionAutoScrollTimer.Start();
    }

    private void StopSelectionAutoScroll()
    {
        _selectionAutoScrollTimer?.Stop();
    }

    private void OnSelectionAutoScrollTick(object? sender, EventArgs e)
    {
        if (!_isSelecting)
        {
            StopSelectionAutoScroll();
            return;
        }

        var scrollViewer = GetOwnerScrollViewer();
        if (scrollViewer is null)
        {
            StopSelectionAutoScroll();
            return;
        }

        var viewportHeight = scrollViewer.Viewport.Height;
        if (viewportHeight <= 0)
        {
            StopSelectionAutoScroll();
            return;
        }

        var deltaY = 0.0;
        if (_selectionPointerViewportPoint.Y < AutoScrollEdgeThreshold)
        {
            deltaY = -CalculateAutoScrollDelta(AutoScrollEdgeThreshold - _selectionPointerViewportPoint.Y);
        }
        else if (_selectionPointerViewportPoint.Y > viewportHeight - AutoScrollEdgeThreshold)
        {
            deltaY = CalculateAutoScrollDelta(_selectionPointerViewportPoint.Y - (viewportHeight - AutoScrollEdgeThreshold));
        }

        if (Math.Abs(deltaY) < 0.1)
        {
            StopSelectionAutoScroll();
            return;
        }

        var maxVerticalOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var nextVerticalOffset = Math.Clamp(scrollViewer.Offset.Y + deltaY, 0, maxVerticalOffset);
        if (Math.Abs(nextVerticalOffset - scrollViewer.Offset.Y) < 0.1)
        {
            StopSelectionAutoScroll();
            return;
        }

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextVerticalOffset);
        VerticalOffset = nextVerticalOffset;
        var documentPoint = new Point(
            scrollViewer.Offset.X + _selectionPointerViewportPoint.X,
            nextVerticalOffset + _selectionPointerViewportPoint.Y);
        UpdateSelectionActivePosition(HitTestSelectionPosition(documentPoint));
    }

    private static double CalculateAutoScrollDelta(double overshoot)
    {
        var normalizedOvershoot = Math.Clamp(overshoot, 0, AutoScrollEdgeThreshold);
        return Math.Max(4.0, normalizedOvershoot * 0.65);
    }

    private void EndSelectionCapture(IPointer pointer)
    {
        _isSelecting = false;
        StopSelectionAutoScroll();
        if (pointer.Captured == this)
            pointer.Capture(null);
    }

    private readonly record struct SelectionHitResult(SelectionPosition Position, SelectionHitKind Kind);
    private readonly record struct SelectionPosition(int Line, int Column);
    private enum SelectionHitKind
    {
        Empty = 0,
        Text = 1
    }

    private sealed class VisibleTextWindow
    {
        private readonly int[] _lineStarts;

        public VisibleTextWindow(int firstLine, int lastLine, string text)
        {
            FirstLine = firstLine;
            LastLine = lastLine;
            Text = text;
            _lineStarts = BuildLineStarts(firstLine, lastLine, text);
        }

        public int FirstLine { get; }

        public int LastLine { get; }

        public string Text { get; }

        public SelectionPosition Clamp(SelectionPosition position)
        {
            var clampedLine = Math.Clamp(position.Line, FirstLine, LastLine);
            var clampedColumn = Math.Clamp(position.Column, 0, GetLineLength(clampedLine));
            return new SelectionPosition(clampedLine, clampedColumn);
        }

        public int GetLineLength(int lineNumber)
        {
            var lineIndex = Math.Clamp(lineNumber - FirstLine, 0, _lineStarts.Length - 1);
            var lineStart = _lineStarts[lineIndex];
            var lineEnd = lineIndex + 1 < _lineStarts.Length
                ? Math.Max(lineStart, _lineStarts[lineIndex + 1] - 1)
                : Text.Length;
            return Math.Max(0, lineEnd - lineStart);
        }

        public int GetLocalTextIndex(int lineNumber, int column)
        {
            var lineIndex = Math.Clamp(lineNumber - FirstLine, 0, _lineStarts.Length - 1);
            var lineStart = _lineStarts[lineIndex];
            var clampedColumn = Math.Clamp(column, 0, GetLineLength(lineNumber));
            return lineStart + clampedColumn;
        }

        private static int[] BuildLineStarts(int firstLine, int lastLine, string text)
        {
            var lineCount = Math.Max(1, lastLine - firstLine + 1);
            var lineStarts = new int[lineCount];
            var currentLine = 0;

            for (var i = 0; i < text.Length && currentLine + 1 < lineStarts.Length; i++)
            {
                if (text[i] != '\n')
                    continue;

                currentLine++;
                lineStarts[currentLine] = i + 1;
            }

            return lineStarts;
        }
    }
}
