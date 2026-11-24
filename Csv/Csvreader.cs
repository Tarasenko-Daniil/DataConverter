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

    public CsvRecordReader(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _delimiter = options.Delimiter;
        if (_delimiter == '"' || _delimiter == '\r' || _delimiter == '\n')
            throw new ArgumentException("Недопустимый разделитель CSV.", nameof(options));
    }

    public override string FormatName
    {
        get { return "csv"; }
    }

    protected override IEnumerable<DataRecord> ReadCore(TextReader input)
    {
        int line = 1;

        if (input.Peek() == '\uFEFF')
            input.Read();

        string[]? header = null;

        while (true)
        {
            int rowLine;
            List<string?>? fields = ReadRow(input, ref line, out rowLine);

            if (fields == null)
                yield break;

            if (header == null)
            {
                header = ReadHeader(fields, rowLine);
                continue;
            }

            if (fields.Count != header.Length)
            {
                throw new InvalidSourceFormatException("CSV", $"ожидалось столбцов: {header.Length}, получено: {fields.Count}.", rowLine);
            }

            DataRecord record = new DataRecord();

            for (int column = 0; column < header.Length; column++)
            {
                record.SetPath(header[column], fields[column]);
            }

            List<KeyValuePair<string, object?>> topFields = record.Fields;
            foreach (KeyValuePair<string, object?> field in topFields)
            {
                record.Set(field.Key, NormalizeLists(field.Value));
            }

            yield return record;
        }
    }

    private List<string?>? ReadRow(TextReader input, ref int line, out int rowLine)
    {
        while (true)
        {
            rowLine = line;

            if (input.Peek() < 0)
                return null;

            List<string?> fields = new List<string?>();

            while (true)
            {
                string? value;

                if (input.Peek() == '"')
                {
                    value = ReadQuotedField(input, ref line, rowLine);
                }
                else
                {
                    value = ReadPlainField(input);
                }

                fields.Add(value);

                if (input.Peek() == _delimiter)
                {
                    input.Read();
                    continue;
                }

                break;
            }

            FinishLine(input, ref line);

            if (fields.Count == 1 && fields[0] == null)
                continue;

            return fields;
        }
    }

    private string? ReadQuotedField(TextReader input, ref int line, int rowLine)
    {
        input.Read();

        StringBuilder text = new StringBuilder();

        while (true)
        {
            int symbol = input.Read();

            if (symbol < 0)
                throw new InvalidSourceFormatException("CSV", "не закрыта кавычка.", rowLine);

            char ch = (char)symbol;

            if (ch == '"')
            {
                if (input.Peek() == '"')
                {
                    input.Read();
                    text.Append('"');
                    continue;
                }

                break;
            }

            if (ch == '\n')
                line++;

            text.Append(ch);
        }

        int next = input.Peek();
        if (next >= 0 && next != _delimiter && next != '\r' && next != '\n')
        {
            throw new InvalidSourceFormatException("CSV",
                "после закрывающей кавычки ожидался разделитель или конец строки.", line);
        }

        return text.ToString();
    }

    private string? ReadPlainField(TextReader input)
    {
        StringBuilder text = new StringBuilder();

        while (true)
        {
            int next = input.Peek();

            if (next < 0 || next == _delimiter || next == '\r' || next == '\n')
                break;

            text.Append((char)input.Read());
        }

        if (text.Length == 0)
            return null;

        return text.ToString();
    }

    private static void FinishLine(TextReader input, ref int line)
    {
        int next = input.Peek();

        if (next == '\r')
        {
            input.Read();

            if (input.Peek() == '\n')
                input.Read();

            line++;
        }
        else if (next == '\n')
        {
            input.Read();
            line++;
        }
    }

    private static string[] ReadHeader(List<string?> fields, int rowLine)
    {
        string[] header = new string[fields.Count];
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        for (int column = 0; column < header.Length; column++)
        {
            string name = (fields[column] ?? string.Empty).Trim();

            if (name.Length == 0)
                name = "column" + (column + 1).ToString(CultureInfo.InvariantCulture);

            if (names.Add(name) == false)
            {
                throw new InvalidSourceFormatException("CSV", $"повторяющееся имя столбца '{name}'.", rowLine);
            }

            header[column] = name;
        }

        foreach (string name in header)
        {
            for (int dot = name.IndexOf('.'); dot >= 0; dot = name.IndexOf('.', dot + 1))
            {
                string prefix = name.Substring(0, dot);

                if (names.Contains(prefix))
                {
                    throw new InvalidSourceFormatException("CSV", $"конфликт столбцов '{prefix}' и '{name}'.", rowLine);
                }
            }
        }

        return header;
    }

    private static object? NormalizeLists(object? value)
    {
        DataRecord? record = value as DataRecord;

        if (record == null)
            return value;

        List<KeyValuePair<string, object?>> fields = record.Fields;

        foreach (KeyValuePair<string, object?> field in fields)
        {
            record.Set(field.Key, NormalizeLists(field.Value));
        }

        bool isList = fields.Count > 0;

        for (int i = 0; i < fields.Count && isList; i++)
        {
            string expected = i.ToString(CultureInfo.InvariantCulture);
            isList = fields[i].Key == expected;
        }

        if (isList == false)
            return record;

        List<object?> list = new List<object?>(fields.Count);

        foreach (KeyValuePair<string, object?> field in fields)
        {
            list.Add(record.Get(field.Key));
        }

        return list;
    }
}