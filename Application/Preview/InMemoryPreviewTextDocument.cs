namespace DevProjex.Application.Preview;

public sealed class InMemoryPreviewTextDocument : IPreviewTextDocument
{
    private readonly string _text;
    private readonly List<int> _lineStarts = [0];
    private readonly int _lineCount;
    private readonly int _maxLineLength;

    public InMemoryPreviewTextDocument(string? text)
    {
        _text = text ?? string.Empty;
        (_lineCount, _maxLineLength) = BuildLineMetadata(_text, _lineStarts);
    }

    public int LineCount => _lineCount;

    public int MaxLineLength => _maxLineLength;

    public long CharacterCount => _text.Length;

    public string GetLineRangeText(int firstLine, int lastLine)
    {
        if (_text.Length == 0)
            return string.Empty;

        var normalizedFirstLine = Math.Max(1, firstLine);
        var normalizedLastLine = Math.Min(_lineCount, Math.Max(normalizedFirstLine, lastLine));
        if (normalizedLastLine < normalizedFirstLine)
            return string.Empty;

        var linesCount = normalizedLastLine - normalizedFirstLine + 1;
        var estimatedLineLength = Math.Max(12, Math.Min(_maxLineLength, 256));
        var builder = new StringBuilder(linesCount * (estimatedLineLength + 1));

        for (var lineIndex = normalizedFirstLine - 1; lineIndex <= normalizedLastLine - 1; lineIndex++)
        {
            AppendLineSlice(builder, _text, _lineStarts, lineIndex);
            if (lineIndex < normalizedLastLine - 1)
                builder.Append('\n');
        }

        return builder.ToString();
    }

    public void Dispose()
    {
    }

    private static (int LineCount, int MaxLineLength) BuildLineMetadata(string text, List<int> lineStarts)
    {
        if (text.Length == 0)
            return (1, 0);

        var currentLineLength = 0;
        var maxLineLength = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\n')
            {
                if (currentLineLength > maxLineLength)
                    maxLineLength = currentLineLength;

                currentLineLength = 0;
                lineStarts.Add(i + 1);
                continue;
            }

            if (ch != '\r')
                currentLineLength++;
        }

        if (currentLineLength > maxLineLength)
            maxLineLength = currentLineLength;

        return (Math.Max(1, lineStarts.Count), maxLineLength);
    }

    private static void AppendLineSlice(StringBuilder builder, string text, IReadOnlyList<int> lineStarts, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lineStarts.Count)
            return;

        var start = lineStarts[lineIndex];
        var nextStart = lineIndex + 1 < lineStarts.Count
            ? lineStarts[lineIndex + 1]
            : text.Length;

        var endExclusive = lineIndex + 1 < lineStarts.Count
            ? Math.Max(start, nextStart - 1)
            : nextStart;

        if (endExclusive > start && text[endExclusive - 1] == '\r')
            endExclusive--;

        if (endExclusive > start)
            builder.Append(text.AsSpan(start, endExclusive - start));
    }
}
