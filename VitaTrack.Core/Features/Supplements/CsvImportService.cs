using System.Text;

namespace VitaTrack.Core.Features.Supplements;

public class CsvImportService : ICsvImportService
{
    private const int MaxRows = 20;
    private static readonly string[] ExpectedHeaders = ["Name", "Brand", "DailyDose", "ManufacturerUrl", "Cost", "ServingsPerBottle"];

    private readonly ICsvRowRule[] _rules;

    public CsvImportService()
    {
        _rules =
        [
            new CsvRequiredFieldsRule(),
            new CsvLengthLimitsRule(),
            new CsvCostRule(),
            new CsvServingsRule()
        ];
    }

    public async Task<CsvParseResult> ParseAsync(Stream csvStream)
    {
        var rows = new List<CsvSupplementRow>();
        var errors = new List<CsvParseError>();

        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;
        string? headerLine = null;

        while (await reader.ReadLineAsync() is { } line)
        {
            lineNumber++;
            line = line.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (headerLine == null)
            {
                headerLine = line;
                var headerError = ValidateHeader(line);
                if (headerError != null)
                {
                    errors.Add(new CsvParseError(0, headerError));
                    return new CsvParseResult(rows, errors);
                }
                continue;
            }

            if (rows.Count >= MaxRows)
            {
                errors.Add(new CsvParseError(0, $"CSV exceeds maximum of {MaxRows} rows"));
                return new CsvParseResult(new List<CsvSupplementRow>(), errors);
            }

            var fields = ParseCsvLine(line);
            var rowResult = ParseRow(lineNumber, fields);
            if (rowResult.Error != null)
                errors.Add(rowResult.Error);
            else if (rowResult.Row != null)
                rows.Add(rowResult.Row);
        }

        if (headerLine == null)
            errors.Add(new CsvParseError(0, "CSV file is empty"));

        return new CsvParseResult(rows, errors);
    }

    private static string? ValidateHeader(string headerLine)
    {
        var headers = ParseCsvLine(headerLine);
        if (headers.Length != ExpectedHeaders.Length)
            return $"Expected {ExpectedHeaders.Length} columns, found {headers.Length}";

        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            if (!string.Equals(headers[i].Trim(), ExpectedHeaders[i], StringComparison.OrdinalIgnoreCase))
                return $"Column {i + 1}: expected '{ExpectedHeaders[i]}', found '{headers[i].Trim()}'";
        }

        return null;
    }

    private (CsvSupplementRow? Row, CsvParseError? Error) ParseRow(int lineNumber, string[] fields)
    {
        var context = new CsvRowContext(
            lineNumber,
            fields.Length > 0 ? fields[0].Trim() : string.Empty,
            fields.Length > 1 ? fields[1].Trim() : string.Empty,
            fields.Length > 2 ? fields[2].Trim() : string.Empty,
            fields.Length > 3 ? fields[3].Trim() : string.Empty,
            fields.Length > 4 ? fields[4].Trim() : string.Empty,
            fields.Length > 5 ? fields[5].Trim() : string.Empty);

        foreach (var rule in _rules)
        {
            var error = rule.Apply(context);
            if (error != null)
                return (null, error);
        }

        var manufacturerUrl = string.IsNullOrWhiteSpace(context.ManufacturerUrl) ? null : context.ManufacturerUrl;
        var row = new CsvSupplementRow(lineNumber, context.Name, context.Brand, context.DailyDose,
            manufacturerUrl, context.Cost, context.ServingsPerBottle);
        return (row, null);
    }

    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
