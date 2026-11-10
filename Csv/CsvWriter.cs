using System.Text;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;
 
namespace DataConverter.Csv;
 
public sealed class CsvRecordWriter : RecordWriterBase
{
    private const string LineBreak = "\r\n";
    private const string FormulaStarts = "=+-@\t\r";
 
    private readonly char _delimiter;
    private readonly bool _sanitizeFormulas;
 
    public CsvRecordWriter(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
 
        _delimiter = options.Delimiter;
        _sanitizeFormulas = options.SanitizeFormulas;
 
        if (_delimiter is '"' or '\r' or '\n')
            throw new ArgumentException("Недопустимый разделитель CSV.", nameof(options));
    }
 
    public override string FormatName => "csv";
 
    public override void WriteCore(IReadOnlyList<DataRecord> records, TextWriter output)
    {
        var header = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, object?>>(records.Count);
 
        foreach (var record in records)
        {
            var pairs = new List<KeyValuePair<string, object?>>();
            Flatten(string.Empty, record, pairs);
 
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
 
            foreach (var pair in pairs)
            {
                if (!row.TryAdd(pair.Key, pair.Value))
                    throw new WriteException($"Конфликт имён столбцов CSV: '{pair.Key}'.");
 
                if (known.Add(pair.Key))
                    header.Add(pair.Key);
            }
 
            rows.Add(row);
        }
 
        if (header.Count == 0)
            return;
 
        var sb = new StringBuilder();
 
        AppendRow(sb, header.Select(Escape));
 
        foreach (var row in rows)
        {
            AppendRow(sb, header.Select(column => row.TryGetValue(column, out var value) ? FormatCell(value) : string.Empty));
        }
 
        try
        {
            output.Write(sb.ToString());
        }
        catch (IOException ex)
        {
            throw new WriteException("Ошибка записи CSV: " + ex.Message, ex);
        }
    }
 
    private static void Flatten(string prefix, object? value, List<KeyValuePair<string, object?>> target)
    {
        switch (value)
        {
            case DataRecord record:
                if (record.Count == 0 && prefix.Length > 0)
                {
                    target.Add(new(prefix, null));
                    break;
                }
 
                foreach (var field in record.Fields)
                {
                    Flatten(prefix.Length == 0 ? field.Key : prefix + "." + field.Key, field.Value, target);
                }
 
                break;
 
            case List<object?> list:
                if (list.Count == 0)
                {
                    target.Add(new(prefix, null));
                    break;
                }
 
                for (var i = 0; i < list.Count; i++)
                {
                    Flatten(prefix + "." + i, list[i], target);
                }
 
                break;
 
            default:
                target.Add(new(prefix, value));
                break;
        }
    }
 
    private string FormatCell(object? value)
    {
        if (value is null)
            return string.Empty;
 
        var text = ValueFormatter.Format(value);
 
        if (_sanitizeFormulas && value is string && text.Length > 0 && FormulaStarts.Contains(text[0]))
            text = "'" + text;
 
        return Escape(text);
    }
 
    private string Escape(string text)
    {
        var needQuotes = text.Length == 0
                         || text.IndexOfAny(new[] { _delimiter, '"', '\r', '\n' }) >= 0
                         || char.IsWhiteSpace(text[0])
                         || char.IsWhiteSpace(text[^1]);
 
        if (!needQuotes)
            return text;
 
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
 
    private void AppendRow(StringBuilder sb, IEnumerable<string> cells)
    {
        var first = true;
 
        foreach (var cell in cells)
        {
            if (!first)
                sb.Append(_delimiter);
 
            sb.Append(cell);
            first = false;
        }
 
        sb.Append(LineBreak);
    }
}