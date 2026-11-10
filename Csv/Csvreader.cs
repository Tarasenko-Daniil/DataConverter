using System.Globalization;
using System.Text;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;
 
namespace DataConverter.Csv;
 
public sealed class CsvRecordReader : RecordReaderBase
{
    private readonly char _delimiter;
 
    private readonly record struct CsvRow(int Line, List<string?> Fields);
 
    public CsvRecordReader(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
 
        _delimiter = options.Delimiter;
        if (_delimiter is '"' or '\r' or '\n')
            throw new ArgumentException("Недопустимый разделитель CSV.", nameof(options));
    }
 
    public override string FormatName => "csv";
 
    public override IReadOnlyList<DataRecord> ReadCore(TextReader input)
    {
        var text = input.ReadToEnd();
        var rows = Parse(text);
 
        if (rows.Count == 0)
            return new List<DataRecord>();
 
        var header = ReadHeader(rows[0]);
        var records = new List<DataRecord>(rows.Count - 1);
 
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
 
            if (row.Fields.Count != header.Length)
                throw new InvalidSourceFormatException("CSV",
                    $"ожидалось столбцов: {header.Length}, получено: {row.Fields.Count}.", row.Line);
 
            var record = new DataRecord();
 
            for (var c = 0; c < header.Length; c++)
            {
                record.SetPath(header[c], row.Fields[c]);
            }
 
            foreach (var field in record.Fields)
            {
                record.Set(field.Key, NormalizeLists(field.Value));
            }
 
            records.Add(record);
        }
 
        return records;
    }
 
    private static string[] ReadHeader(CsvRow row)
    {
        var header = new string[row.Fields.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
 
        for (var c = 0; c < header.Length; c++)
        {
            var name = (row.Fields[c] ?? string.Empty).Trim();
 
            if (name.Length == 0)
                name = $"column{c + 1}";
 
            if (!names.Add(name))
                throw new InvalidSourceFormatException("CSV", $"повторяющееся имя столбца '{name}'.", row.Line);
 
            header[c] = name;
        }
 
        foreach (var name in header)
        {
            for (var dot = name.IndexOf('.'); dot >= 0; dot = name.IndexOf('.', dot + 1))
            {
                if (names.Contains(name[..dot]))
                    throw new InvalidSourceFormatException("CSV",
                        $"конфликт столбцов '{name[..dot]}' и '{name}'.", row.Line);
            }
        }
 
        return header;
    }
 
    private static object? NormalizeLists(object? value)
    {
        if (value is not DataRecord record)
            return value;
 
        var fields = record.Fields;
 
        foreach (var field in fields)
        {
            record.Set(field.Key, NormalizeLists(field.Value));
        }
 
        var isList = fields.Count > 0;
        for (var i = 0; i < fields.Count && isList; i++)
        {
            isList = fields[i].Key == i.ToString(CultureInfo.InvariantCulture);
        }
 
        if (!isList)
            return record;
 
        var list = new List<object?>(fields.Count);
        foreach (var field in fields)
        {
            list.Add(record.Get(field.Key));
        }
 
        return list;
    }
 

    private List<CsvRow> Parse(string text)
    {
        var rows = new List<CsvRow>();
        var i = 0;
        var line = 1;
 
        if (text.Length > 0 && text[0] == '\uFEFF')
            i = 1;
 
        while (i < text.Length)
        {
            var rowLine = line;
            var fields = new List<string?>();
 
            while (true)
            {
                string? value;
 
                if (i < text.Length && text[i] == '"')
                {
                    var sb = new StringBuilder();
                    i++;
 
                    while (true)
                    {
                        if (i >= text.Length)
                            throw new InvalidSourceFormatException("CSV", "не закрыта кавычка.", rowLine);
 
                        var c = text[i];
 
                        if (c == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                sb.Append('"');
                                i += 2;
                                continue;
                            }
 
                            i++;
                            break;
                        }
 
                        if (c == '\n')
                            line++;
 
                        sb.Append(c);
                        i++;
                    }
 
                    if (i < text.Length && text[i] != _delimiter && text[i] != '\r' && text[i] != '\n')
                        throw new InvalidSourceFormatException("CSV",
                            "после закрывающей кавычки ожидался разделитель или конец строки.", line);
 
                    value = sb.ToString();
                }
                else
                {
                    var start = i;
 
                    while (i < text.Length && text[i] != _delimiter && text[i] != '\r' && text[i] != '\n')
                    {
                        i++;
                    }
 
                    value = i == start ? null : text.Substring(start, i - start);
                }
 
                fields.Add(value);
 
                if (i < text.Length && text[i] == _delimiter)
                {
                    i++;
                    continue;
                }
 
                break;
            }
 
            if (i < text.Length)
            {
                if (text[i] == '\r')
                    i++;
                if (i < text.Length && text[i] == '\n')
                    i++;
                line++;
            }
 
            if (fields.Count == 1 && fields[0] is null)
                continue;
 
            rows.Add(new CsvRow(rowLine, fields));
        }
 
        return rows;
    }
}