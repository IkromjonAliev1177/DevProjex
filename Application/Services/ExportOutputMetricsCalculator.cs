namespace DevProjex.Application.Services;

public static class ExportOutputMetricsCalculator
{
	private const string ClipboardBlankLine = "\u00A0";
	private const string NoContentMarker = "[No Content, 0 bytes]";
	private const string WhitespaceMarkerPrefix = "[Whitespace, ";
	private const string WhitespaceMarkerSuffix = " bytes]";
	private static readonly System.Buffers.SearchValues<char> LineBreakCharacters =
		System.Buffers.SearchValues.Create("\r\n");

	public static ExportOutputMetrics FromText(string text)
	{
		if (string.IsNullOrEmpty(text))
			return ExportOutputMetrics.Empty;

		var stats = GetNormalizedTextStats(text.AsSpan());
		int chars = stats.NormalizedChars;
		int lines = stats.LineBreaks + 1;
		int tokens = EstimateTokens(chars);

		return new ExportOutputMetrics(lines, chars, tokens);
	}

	public static ExportOutputMetrics FromContentFiles(IEnumerable<ContentFileMetrics> files)
	{
		var uniquePaths = new HashSet<string>(PathComparer.Default);
		var ordered = new List<ContentFileMetrics>();
		foreach (var file in files)
		{
			if (string.IsNullOrWhiteSpace(file.Path))
				continue;

			if (!uniquePaths.Add(file.Path))
				continue;

			ordered.Add(file);
		}

		if (ordered.Count == 0)
			return ExportOutputMetrics.Empty;

		ordered.Sort(static (left, right) => PathComparer.Default.Compare(left.Path, right.Path));

		// Status metrics use normalized line-break counting (CRLF/CR/LF => one character).
		const int normalizedNewLineChars = 1;
		int chars = 0;
		int lineBreaks = 0;
		int trailingLineBreakChars = 0;
		int trailingLineBreaks = 0;
		bool anyWritten = false;

		foreach (var file in ordered)
		{
			if (anyWritten)
			{
				AppendLiteralLine(
					ClipboardBlankLine,
					normalizedNewLineChars,
					ref chars,
					ref lineBreaks,
					ref trailingLineBreakChars,
					ref trailingLineBreaks);
				AppendLiteralLine(
					ClipboardBlankLine,
					normalizedNewLineChars,
					ref chars,
					ref lineBreaks,
					ref trailingLineBreakChars,
					ref trailingLineBreaks);
			}

			anyWritten = true;

			AppendLiteralLine(
				$"{file.Path}:",
				normalizedNewLineChars,
				ref chars,
				ref lineBreaks,
				ref trailingLineBreakChars,
				ref trailingLineBreaks);
			AppendLiteralLine(
				ClipboardBlankLine,
				normalizedNewLineChars,
				ref chars,
				ref lineBreaks,
				ref trailingLineBreakChars,
				ref trailingLineBreaks);

			if (file.IsEmpty)
			{
				AppendLiteralLine(
					NoContentMarker,
					normalizedNewLineChars,
					ref chars,
					ref lineBreaks,
					ref trailingLineBreakChars,
					ref trailingLineBreaks);
				continue;
			}

			if (file.IsWhitespaceOnly)
			{
				AppendLiteralLine(
					$"{WhitespaceMarkerPrefix}{file.SizeBytes}{WhitespaceMarkerSuffix}",
					normalizedNewLineChars,
					ref chars,
					ref lineBreaks,
					ref trailingLineBreakChars,
					ref trailingLineBreaks);
				continue;
			}

			if (file.IsEstimated)
			{
				// SelectedContentExportService writes an empty content line for estimated files.
				// It becomes relevant for intermediate files, while trailing line-break trim is handled below.
				AppendRenderedLine(
					renderedChars: 0,
					internalLineBreaks: 0,
					newLineChars: normalizedNewLineChars,
					chars: ref chars,
					lineBreaks: ref lineBreaks,
					trailingLineBreakChars: ref trailingLineBreakChars,
					trailingLineBreaks: ref trailingLineBreaks);
				continue;
			}

			int internalLineBreaks = Math.Max(0, file.LineCount - 1);
			int trimmedLineBreaks = Math.Max(0, internalLineBreaks - file.TrailingNewlineLineBreaks);
			int normalizedChars = Math.Max(0, file.CharCount - file.CrLfPairCount);
			int trimmedChars = Math.Max(0, normalizedChars - file.TrailingNewlineLineBreaks);

			AppendRenderedLine(
				renderedChars: trimmedChars,
				internalLineBreaks: trimmedLineBreaks,
				newLineChars: normalizedNewLineChars,
				chars: ref chars,
				lineBreaks: ref lineBreaks,
				trailingLineBreakChars: ref trailingLineBreakChars,
				trailingLineBreaks: ref trailingLineBreaks);
		}

		// SelectedContentExportService trims trailing CR/LF from the final result.
		chars = Math.Max(0, chars - trailingLineBreakChars);
		lineBreaks = Math.Max(0, lineBreaks - trailingLineBreaks);

		if (chars == 0)
			return ExportOutputMetrics.Empty;

		int lines = lineBreaks + 1;
		int tokens = EstimateTokens(chars);
		return new ExportOutputMetrics(lines, chars, tokens);
	}

	private static void AppendLiteralLine(
		string text,
		int newLineChars,
		ref int chars,
		ref int lineBreaks,
		ref int trailingLineBreakChars,
		ref int trailingLineBreaks)
	{
		AppendRenderedLine(
			renderedChars: text.Length,
			internalLineBreaks: 0,
			newLineChars: newLineChars,
			chars: ref chars,
			lineBreaks: ref lineBreaks,
			trailingLineBreakChars: ref trailingLineBreakChars,
			trailingLineBreaks: ref trailingLineBreaks);
	}

	private static void AppendRenderedLine(
		int renderedChars,
		int internalLineBreaks,
		int newLineChars,
		ref int chars,
		ref int lineBreaks,
		ref int trailingLineBreakChars,
		ref int trailingLineBreaks)
	{
		chars += renderedChars + newLineChars;
		lineBreaks += internalLineBreaks + 1;

		if (renderedChars == 0 && internalLineBreaks == 0)
		{
			trailingLineBreakChars += newLineChars;
			trailingLineBreaks++;
			return;
		}

		trailingLineBreakChars = newLineChars;
		trailingLineBreaks = 1;
	}

	private static int EstimateTokens(int chars) =>
		chars <= 0 ? 0 : (chars + 3) / 4;

	private static NormalizedTextStats GetNormalizedTextStats(ReadOnlySpan<char> text)
	{
		var normalizedChars = 0;
		var lineBreaks = 0;
		var index = 0;

		// Scan non-line-break segments in bulk to reduce per-char branching on hot paths.
		while (index < text.Length)
		{
			var remaining = text[index..];
			var breakOffset = remaining.IndexOfAny(LineBreakCharacters);
			if (breakOffset < 0)
			{
				normalizedChars += remaining.Length;
				break;
			}

			normalizedChars += breakOffset + 1;
			index += breakOffset;

			if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
			{
				// CRLF contributes a single normalized line-break character.
				index++;
			}

			lineBreaks++;
			index++;
		}

		return new NormalizedTextStats(normalizedChars, lineBreaks);
	}

	private readonly record struct NormalizedTextStats(int NormalizedChars, int LineBreaks);
}

public readonly record struct ContentFileMetrics(
	string Path,
	long SizeBytes,
	int LineCount,
	int CharCount,
	bool IsEmpty,
	bool IsWhitespaceOnly,
	bool IsEstimated = false,
	int CrLfPairCount = 0,
	int TrailingNewlineChars = 0,
	int TrailingNewlineLineBreaks = 0);

public readonly record struct ExportOutputMetrics(int Lines, int Chars, int Tokens)
{
	public static ExportOutputMetrics Empty { get; } = new(0, 0, 0);
}
