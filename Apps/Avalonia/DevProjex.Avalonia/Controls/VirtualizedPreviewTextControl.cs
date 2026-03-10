namespace DevProjex.Avalonia.Controls;

/// <summary>
/// Draws only visible preview text lines for large payloads.
/// This avoids creating and layouting a huge TextBlock visual tree.
/// </summary>
public sealed class VirtualizedPreviewTextControl : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(VerticalOffset));

    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<VirtualizedPreviewTextControl, double>(nameof(ViewportHeight));

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

    private const int RenderBufferLines = 3;
    private readonly List<int> _lineStarts = [0];
    private int _lineCount = 1;
    private int _maxLineLength;

    static VirtualizedPreviewTextControl()
    {
        AffectsRender<VirtualizedPreviewTextControl>(
            TextProperty,
            VerticalOffsetProperty,
            ViewportHeightProperty,
            TopPaddingProperty,
            BottomPaddingProperty,
            LeftPaddingProperty,
            RightPaddingProperty,
            TextFontFamilyProperty,
            TextFontSizeProperty,
            TextBrushProperty);

        AffectsMeasure<VirtualizedPreviewTextControl>(
            TextProperty,
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
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
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

    public VirtualizedPreviewTextControl()
    {
        RebuildTextLayoutMetadata();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var typeface = ResolveTypeface();
        var lineHeight = ResolveLineHeight(typeface);
        var width = CalculateRequiredWidth(typeface);
        var height = Math.Ceiling(TopPadding + BottomPadding + (_lineCount * lineHeight));

        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_lineCount <= 0)
            return;

        var typeface = ResolveTypeface();
        var lineHeight = ResolveLineHeight(typeface);
        if (lineHeight <= 0)
            return;

        var viewportTop = Math.Max(0, VerticalOffset);
        var viewportHeight = ViewportHeight > 0 ? ViewportHeight : Bounds.Height;
        var firstVisibleLine = Math.Max(1, (int)Math.Floor((viewportTop - TopPadding) / lineHeight) + 1);
        var visibleLineCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / lineHeight));
        var lastVisibleLine = Math.Min(_lineCount, firstVisibleLine + visibleLineCount - 1);

        firstVisibleLine = Math.Max(1, firstVisibleLine - RenderBufferLines);
        lastVisibleLine = Math.Min(_lineCount, lastVisibleLine + RenderBufferLines);
        if (lastVisibleLine < firstVisibleLine)
            return;

        var text = BuildVisibleLinesText(firstVisibleLine, lastVisibleLine);
        if (text.Length == 0)
            return;

        var formattedText = BuildFormattedText(text, typeface);
        var originY = TopPadding + (firstVisibleLine - 1) * lineHeight;
        context.DrawText(formattedText, new Point(LeftPadding, originY));
    }

    private void RebuildTextLayoutMetadata()
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

    private string BuildVisibleLinesText(int firstVisibleLine, int lastVisibleLine)
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0)
            return string.Empty;

        var linesCount = Math.Max(0, lastVisibleLine - firstVisibleLine + 1);
        var estimatedLineLength = Math.Max(12, Math.Min(_maxLineLength, 256));
        var builder = new StringBuilder(linesCount * (estimatedLineLength + 1));

        for (var lineIndex = firstVisibleLine - 1; lineIndex <= lastVisibleLine - 1; lineIndex++)
        {
            AppendLineSlice(builder, text, lineIndex);
            if (lineIndex < lastVisibleLine - 1)
                builder.Append('\n');
        }

        return builder.ToString();
    }

    private void AppendLineSlice(StringBuilder builder, string text, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lineStarts.Count)
            return;

        var start = _lineStarts[lineIndex];
        var nextStart = lineIndex + 1 < _lineStarts.Count
            ? _lineStarts[lineIndex + 1]
            : text.Length;

        var endExclusive = lineIndex + 1 < _lineStarts.Count
            ? Math.Max(start, nextStart - 1)
            : nextStart;

        if (endExclusive > start && text[endExclusive - 1] == '\r')
            endExclusive--;

        if (endExclusive > start)
            builder.Append(text.AsSpan(start, endExclusive - start));
    }

    private Typeface ResolveTypeface() =>
        new(TextFontFamily ?? FontFamily.Default, FontStyle.Normal, FontWeight.Normal);

    private double CalculateRequiredWidth(Typeface typeface)
    {
        var sample = BuildFormattedText("W", typeface);
        var glyphWidth = Math.Max(1.0, sample.Width);
        var contentWidth = _maxLineLength * glyphWidth;
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
}
