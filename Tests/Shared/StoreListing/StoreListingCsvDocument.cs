using Microsoft.VisualBasic.FileIO;

namespace DevProjex.Tests.Shared.StoreListing;

internal sealed class StoreListingCsvDocument
{
    private StoreListingCsvDocument(
        string csvPath,
        IReadOnlyList<string> headers,
        IReadOnlyList<StoreListingCsvRow> rows)
    {
        CsvPath = csvPath;
        Headers = headers;
        Rows = rows;
        // Real Partner Center exports may contain empty spacer-like rows.
        // They should stay in the raw row list so schema/order comparisons can still see them,
        // but they must not break the field index used by the validator rules.
        RowsByField = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Field))
            .GroupBy(row => row.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    internal string CsvPath { get; }

    internal IReadOnlyList<string> Headers { get; }

    internal IReadOnlyList<StoreListingCsvRow> Rows { get; }

    internal IReadOnlyDictionary<string, StoreListingCsvRow> RowsByField { get; }

    internal static StoreListingCsvDocument Load(string csvPath)
    {
        using var parser = new TextFieldParser(csvPath);

        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        var headers = parser.ReadFields()
            ?? throw new InvalidOperationException($"CSV '{csvPath}' does not contain a header row.");

        var rows = new List<StoreListingCsvRow>();

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null)
            {
                continue;
            }

            var rowValues = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var index = 0; index < headers.Length; index++)
            {
                var value = index < fields.Length ? fields[index] : string.Empty;
                rowValues[headers[index]] = value;
            }

            rows.Add(new StoreListingCsvRow(rowValues));
        }

        return new StoreListingCsvDocument(csvPath, headers, rows);
    }
}

internal sealed class StoreListingCsvRow
{
    private readonly IReadOnlyDictionary<string, string> _values;

    internal StoreListingCsvRow(IReadOnlyDictionary<string, string> values)
    {
        _values = values;
    }

    internal string Field => GetValue("Field");

    internal IReadOnlyDictionary<string, string> Values => _values;

    internal string GetValue(string columnName)
    {
        return _values.TryGetValue(columnName, out var value) ? value : string.Empty;
    }
}
