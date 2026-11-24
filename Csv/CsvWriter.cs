using System.Globalization;
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
    private readonly char[] _specialChars;

    public CsvRecordWriter(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _delimiter = options.Delimiter;
        _sanitizeFormulas = options.SanitizeFormulas;

        if (_delimiter == '"' || _delimiter == '\r' || _delimiter == '\n')
            throw new ArgumentException("Недопустимый разделитель CSV.", nameof(options));

        _specialChars = new char[] { _delimiter, '"', '\r', '\n' };
    }

    public override string FormatName
    {
        get { return "csv"; }
    }

    protected override int WriteCore(IEnumerable<DataRecord> records, Stream outputStream)
    {
        using StreamWriter output = new StreamWriter(outputStream, new UTF8Encoding(false), 8192, true);

        List<string>? header = null;
        HashSet<string> headerNames = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;

        foreach (DataRecord record in records)
        {
            count++;

            List<KeyValuePair<string, object?>> fields = new List<KeyValuePair<string, object?>>();
            Flatten(string.Empty, record, fields);

            if (header == null || (header.Count == 0 && fields.Count > 0))
            {
                header = new List<string>();
                headerNames.Clear();

                foreach (KeyValuePair<string, object?> field in fields)
                {
                    if (headerNames.Add(field.Key))
                        header.Add(field.Key);
                }

                if (header.Count > 0)
                    WriteHeader(output, header);
            }

            if (header.Count == 0)
                continue;

            Dictionary<string, object?> row = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, object?> field in fields)
            {
                if (row.ContainsKey(field.Key))
                {
                    throw new WriteException($"Конфликт имён столбцов CSV: '{field.Key}' (запись №{count}).");
                }

                if (headerNames.Contains(field.Key) == false)
                {
                    throw new WriteException($"В записи №{count} есть поле '{field.Key}', которого не было в первой записи. " +
                                             "Заголовок CSV нельзя расширить в потоковом режиме: задайте список колонок через маппинг.");
                }

                row[field.Key] = field.Value;
            }

            WriteRow(output, header, row);
        }

        return count;
    }

    private static void Flatten(string prefix, object? value, List<KeyValuePair<string, object?>> target)
    {
        DataRecord? record = value as DataRecord;

        if (record != null)
        {
            if (record.Count == 0 && prefix.Length > 0)
            {
                target.Add(new KeyValuePair<string, object?>(prefix, null));
                return;
            }

            List<KeyValuePair<string, object?>> fields = record.Fields;

            foreach (KeyValuePair<string, object?> field in fields)
            {
                string name;
                if (prefix.Length == 0)
                    name = field.Key;
                else
                    name = prefix + "." + field.Key;

                Flatten(name, field.Value, target);
            }

            return;
        }

        List<object?>? list = value as List<object?>;

        if (list != null)
        {
            if (list.Count == 0)
            {
                target.Add(new KeyValuePair<string, object?>(prefix, null));
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                string name = prefix + "." + i.ToString(CultureInfo.InvariantCulture);
                Flatten(name, list[i], target);
            }

            return;
        }

        target.Add(new KeyValuePair<string, object?>(prefix, value));
    }

    private void WriteHeader(TextWriter output, List<string> header)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (i > 0)
                output.Write(_delimiter);

            output.Write(FormatCell(header[i]));
        }

        output.Write(LineBreak);
    }

    private void WriteRow(TextWriter output, List<string> header, Dictionary<string, object?> row)
    {
        for (int i = 0; i < header.Count; i++)
        {
            if (i > 0)
                output.Write(_delimiter);

            object? value;
            if (row.TryGetValue(header[i], out value))
                output.Write(FormatCell(value));
        }

        output.Write(LineBreak);
    }

    private string FormatCell(object? value)
    {
        if (value == null)
            return string.Empty;

        string text = ValueFormatter.Format(value);

        if (_sanitizeFormulas && value is string)
        {
            if (text.Length > 0 && FormulaStarts.Contains(text[0]))
                text = "'" + text;
        }

        return Escape(text);
    }

    private string Escape(string text)
    {
        bool needQuotes = text.Length == 0 || text.IndexOfAny(_specialChars) >= 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[text.Length - 1]);

        if (needQuotes == false)
            return text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}