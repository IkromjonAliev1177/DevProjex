namespace DevProjex.Infrastructure.ResourceStore;

public sealed class HelpContentProvider
{
    private readonly Lazy<IReadOnlyDictionary<AppLanguage, string>> _cache = new(LoadAll);

    public string GetHelpBody(AppLanguage language)
    {
        var cache = _cache.Value;
        return cache.TryGetValue(language, out var body) ? body : cache[AppLanguage.En];
    }

    public static string ToPlainText(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
            return string.Empty;

        var lines = rawBody.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder(rawBody.Length);

        foreach (var line in lines)
        {
            builder.AppendLine(FormatPlainTextLine(line));
        }

        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyDictionary<AppLanguage, string> LoadAll()
    {
        var assembly = typeof(Marker).Assembly;
        return new Dictionary<AppLanguage, string>
        {
            [AppLanguage.Ru] = Load(assembly, "ru"),
            [AppLanguage.En] = Load(assembly, "en"),
            [AppLanguage.Uz] = Load(assembly, "uz"),
            [AppLanguage.Tg] = Load(assembly, "tg"),
            [AppLanguage.Kk] = Load(assembly, "kk"),
            [AppLanguage.Fr] = Load(assembly, "fr"),
            [AppLanguage.De] = Load(assembly, "de"),
            [AppLanguage.It] = Load(assembly, "it")
        };
    }

    private static string Load(Assembly assembly, string code)
    {
        var resourceName = $"DevProjex.Assets.HelpContent.help.{code}.txt";
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            var fallbackName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith($".HelpContent.help.{code}.txt", StringComparison.OrdinalIgnoreCase));

            stream = fallbackName is null
                ? null
                : assembly.GetManifestResourceStream(fallbackName);
        }

        if (stream is null)
            throw new InvalidOperationException($"Help content resource not found: {resourceName}");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string FormatPlainTextLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        var trimmed = line.Trim();

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            return StripInlineMarkers(trimmed[3..]);

        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            return StripInlineMarkers(trimmed[4..]);

        if (trimmed.StartsWith("* ", StringComparison.Ordinal) || trimmed.StartsWith("- ", StringComparison.Ordinal))
        {
            var indent = line.Length - line.TrimStart().Length;
            var plainIndent = indent >= 2 ? "  " : string.Empty;
            return $"{plainIndent}- {StripInlineMarkers(trimmed[2..])}";
        }

        if (IsNumberedListItem(trimmed))
            return StripInlineMarkers(trimmed);

        return StripInlineMarkers(trimmed);
    }

    private static bool IsNumberedListItem(string line)
    {
        var dotIndex = line.IndexOf(')');
        return dotIndex > 0 &&
               dotIndex <= 4 &&
               char.IsDigit(line[0]) &&
               dotIndex + 1 < line.Length &&
               line[dotIndex + 1] == ' ';
    }

    // Help files use light markdown-like markers for rendering. Clipboard output should stay plain and readable.
    private static string StripInlineMarkers(string text) => text.Replace("`", string.Empty);
}
