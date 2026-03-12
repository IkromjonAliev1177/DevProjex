namespace DevProjex.Application.Preview;

public readonly record struct PreviewSelectionRange(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    public bool IsCollapsed =>
        StartLine == EndLine &&
        StartColumn == EndColumn;

    public PreviewSelectionRange Normalize()
    {
        var startLine = Math.Max(1, StartLine);
        var endLine = Math.Max(1, EndLine);
        var startColumn = Math.Max(0, StartColumn);
        var endColumn = Math.Max(0, EndColumn);

        return Compare(startLine, startColumn, endLine, endColumn) <= 0
            ? new PreviewSelectionRange(startLine, startColumn, endLine, endColumn)
            : new PreviewSelectionRange(endLine, endColumn, startLine, startColumn);
    }

    private static int Compare(int leftLine, int leftColumn, int rightLine, int rightColumn)
    {
        var lineComparison = leftLine.CompareTo(rightLine);
        return lineComparison != 0
            ? lineComparison
            : leftColumn.CompareTo(rightColumn);
    }
}
