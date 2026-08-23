using System.Globalization;
using System.Text;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public class CsvImportService : ICsvImportService
{
    private const int MaxRows = 20;
    private static readonly string[] ExpectedHeaders = ["Name", "Brand", "DailyDose", "ManufacturerUrl", "Cost", "ServingsPerBottle"];

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

    private const int MaxNameLength = 200;
    private const int MaxBrandLength = 200;
    private const int MaxDailyDoseLength = 200;
    private const int MaxManufacturerUrlLength = 500;

    private static (CsvSupplementRow? Row, CsvParseError? Error) ParseRow(int lineNumber, string[] fields)
    {
        var name = fields.Length > 0 ? fields[0].Trim() : string.Empty;
        var brand = fields.Length > 1 ? fields[1].Trim() : string.Empty;
        var dailyDose = fields.Length > 2 ? fields[2].Trim() : string.Empty;
        var manufacturerUrl = fields.Length > 3 ? fields[3].Trim() : null;
        var costStr = fields.Length > 4 ? fields[4].Trim() : null;
        var servingsStr = fields.Length > 5 ? fields[5].Trim() : null;

        if (string.IsNullOrWhiteSpace(name))
            return (null, new CsvParseError(lineNumber, "Missing required field: Name"));
        if (string.IsNullOrWhiteSpace(brand))
            return (null, new CsvParseError(lineNumber, "Missing required field: Brand"));
        if (string.IsNullOrWhiteSpace(dailyDose))
            return (null, new CsvParseError(lineNumber, "Missing required field: DailyDose"));

        if (name.Length > MaxNameLength)
            return (null, new CsvParseError(lineNumber, $"Name exceeds {MaxNameLength} characters"));
        if (brand.Length > MaxBrandLength)
            return (null, new CsvParseError(lineNumber, $"Brand exceeds {MaxBrandLength} characters"));
        if (dailyDose.Length > MaxDailyDoseLength)
            return (null, new CsvParseError(lineNumber, $"DailyDose exceeds {MaxDailyDoseLength} characters"));
        if (!string.IsNullOrWhiteSpace(manufacturerUrl) && manufacturerUrl.Length > MaxManufacturerUrlLength)
            return (null, new CsvParseError(lineNumber, $"ManufacturerUrl exceeds {MaxManufacturerUrlLength} characters"));

        decimal? cost = null;
        if (!string.IsNullOrWhiteSpace(costStr))
        {
            if (decimal.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                if (parsed <= 0)
                    return (null, new CsvParseError(lineNumber, "Cost must be positive"));
                cost = parsed;
            }
            else
                return (null, new CsvParseError(lineNumber, $"Invalid Cost value: '{costStr}'"));
        }

        decimal? servingsPerBottle = null;
        if (!string.IsNullOrWhiteSpace(servingsStr))
        {
            if (decimal.TryParse(servingsStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedServings))
            {
                if (parsedServings <= 0)
                    return (null, new CsvParseError(lineNumber, "ServingsPerBottle must be positive"));
                servingsPerBottle = parsedServings;
            }
            else
                return (null, new CsvParseError(lineNumber, $"Invalid ServingsPerBottle value: '{servingsStr}'"));
        }

        if (string.IsNullOrWhiteSpace(manufacturerUrl))
            manufacturerUrl = null;

        var row = new CsvSupplementRow(lineNumber, name, brand, dailyDose, manufacturerUrl, cost, servingsPerBottle);
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
