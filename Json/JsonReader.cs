using System.Text;
using System.Text.Json;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;

namespace DataConverter.Json;

public sealed class JsonRecordReader : RecordReaderBase
{
    private readonly string? _recordPath;

    public JsonRecordReader(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recordPath = options.RecordPath;
    }

    public override string FormatName
    {
        get { return "json"; }
    }

    protected override IEnumerable<DataRecord> ReadCore(TextReader input)
    {
        JsonScanner scanner = new JsonScanner(input);
        scanner.SkipWhitespace();

        if (scanner.Peek() < 0)
            throw new InvalidSourceFormatException("JSON", "документ пуст.");

        int first = scanner.Peek();

        if (first != '{' && first != '[')
            throw new InvalidSourceFormatException("JSON", "ожидался объект или массив.", scanner.Line);

        if (string.IsNullOrWhiteSpace(_recordPath) == false)
            MoveToValue(scanner, _recordPath!);

        int kind = scanner.Peek();

        if (kind == '[')
        {
            foreach (DataRecord record in ReadArray(scanner))
                yield return record;

            yield break;
        }

        if (kind == '{')
        {
            yield return ReadObject(scanner, "документ");
            yield break;
        }

        throw new InvalidSourceFormatException("JSON", "ожидался массив объектов или объект.", scanner.Line);
    }

    private static IEnumerable<DataRecord> ReadArray(JsonScanner scanner)
    {
        scanner.Read();
        scanner.SkipWhitespace();

        if (scanner.Peek() == ']')
        {
            scanner.Read();
            yield break;
        }

        int index = 0;

        while (true)
        {
            index++;
            yield return ReadObject(scanner, "элемент массива №" + index);

            scanner.SkipWhitespace();
            int separator = scanner.Peek();

            if (separator == ',')
            {
                scanner.Read();
                scanner.SkipWhitespace();
                continue;
            }

            if (separator == ']')
            {
                scanner.Read();
                yield break;
            }

            throw new InvalidSourceFormatException("JSON",
                "ожидалась запятая или закрывающая скобка массива.", scanner.Line);
        }
    }

    private static DataRecord ReadObject(JsonScanner scanner, string place)
    {
        int startLine = scanner.Line;
        string raw = scanner.ReadValueRaw();

        JsonDocument? document = null;

        try
        {
            document = JsonDocument.Parse(raw);

            object? value = ConvertValue(document.RootElement);
            DataRecord? record = value as DataRecord;

            if (record == null)
                throw new InvalidSourceFormatException("JSON", place + " не является объектом.", startLine);

            return record;
        }
        catch (JsonException ex)
        {
            int line = startLine + (int)(ex.LineNumber ?? 0);
            throw new InvalidSourceFormatException("JSON", ex.Message, line, ex);
        }
        finally
        {
            if (document != null)
                document.Dispose();
        }
    }

    private static void MoveToValue(JsonScanner scanner, string path)
    {
        string[] segments = path.Split('.');

        foreach (string segment in segments)
        {
            if (scanner.Peek() != '{')
                throw new RecordPathNotFoundException(path);

            scanner.Read();
            bool found = false;

            while (true)
            {
                scanner.SkipWhitespace();
                int next = scanner.Peek();

                if (next == '}')
                {
                    scanner.Read();
                    break;
                }

                if (next != '"')
                    throw new InvalidSourceFormatException("JSON", "ожидалось имя свойства.", scanner.Line);

                string key = scanner.ReadStringLiteral();

                scanner.SkipWhitespace();

                if (scanner.Peek() != ':')
                    throw new InvalidSourceFormatException("JSON", "ожидалось ':' после имени свойства.", scanner.Line);

                scanner.Read();
                scanner.SkipWhitespace();

                if (key == segment)
                {
                    found = true;
                    break;
                }

                scanner.ReadValueRaw();
                scanner.SkipWhitespace();

                int after = scanner.Peek();

                if (after == ',')
                {
                    scanner.Read();
                    continue;
                }

                if (after == '}')
                {
                    scanner.Read();
                    break;
                }

                throw new InvalidSourceFormatException("JSON", "ожидалась запятая или '}'.", scanner.Line);
            }

            if (found == false)
                throw new RecordPathNotFoundException(path);
        }
    }

    private static object? ConvertValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            DataRecord record = new DataRecord();

            foreach (JsonProperty property in element.EnumerateObject())
            {
                record.Set(property.Name, ConvertValue(property.Value));
            }

            return record;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            List<object?> list = new List<object?>();

            foreach (JsonElement item in element.EnumerateArray())
            {
                list.Add(ConvertValue(item));
            }

            return list;
        }

        if (element.ValueKind == JsonValueKind.String)
            return element.GetString();

        if (element.ValueKind == JsonValueKind.True)
            return true;

        if (element.ValueKind == JsonValueKind.False)
            return false;

        if (element.ValueKind == JsonValueKind.Number)
        {
            long longValue;

            if (element.TryGetInt64(out longValue))
                return longValue;

            decimal decimalValue;

            if (element.TryGetDecimal(out decimalValue))
                return decimalValue;

            return element.GetDouble();
        }

        return null;
    }

    private sealed class JsonScanner
    {
        private const int NothingPeeked = -2;

        private readonly TextReader _input;
        private int _line = 1;
        private int _lookedAhead = NothingPeeked;

        public JsonScanner(TextReader input)
        {
            _input = input;
        }

        public int Line
        {
            get { return _line; }
        }

        public int Peek()
        {
            if (_lookedAhead == NothingPeeked)
                _lookedAhead = _input.Peek();

            return _lookedAhead;
        }

        public int Read()
        {
            int symbol;

            if (_lookedAhead != NothingPeeked)
            {
                symbol = _lookedAhead;
                _lookedAhead = NothingPeeked;

                if (symbol >= 0)
                    _input.Read();
            }
            else
            {
                symbol = _input.Read();
            }

            if (symbol == '\n')
                _line++;

            return symbol;
        }

        public void SkipWhitespace()
        {
            while (true)
            {
                int symbol = Peek();

                if (symbol == ' ' || symbol == '\t' || symbol == '\r' || symbol == '\n')
                {
                    Read();
                    continue;
                }

                return;
            }
        }

        public string ReadStringLiteral()
        {
            Read();

            StringBuilder text = new StringBuilder();

            while (true)
            {
                int symbol = Read();

                if (symbol < 0)
                    throw new InvalidSourceFormatException("JSON", "не закрыта строка.", Line);

                if (symbol == '"')
                    return text.ToString();

                if (symbol != '\\')
                {
                    text.Append((char)symbol);
                    continue;
                }

                int escape = Read();

                if (escape < 0)
                    throw new InvalidSourceFormatException("JSON", "не закрыта строка.", Line);

                if (escape == '"') { text.Append('"'); continue; }
                if (escape == '\\') { text.Append('\\'); continue; }
                if (escape == '/') { text.Append('/'); continue; }
                if (escape == 'b') { text.Append('\b'); continue; }
                if (escape == 'f') { text.Append('\f'); continue; }
                if (escape == 'n') { text.Append('\n'); continue; }
                if (escape == 'r') { text.Append('\r'); continue; }
                if (escape == 't') { text.Append('\t'); continue; }

                if (escape == 'u')
                {
                    text.Append(ReadHexChar());
                    continue;
                }

                throw new InvalidSourceFormatException("JSON",
                    $"неизвестная escape-последовательность \\{(char)escape}.", Line);
            }
        }

        public string ReadValueRaw()
        {
            int first = Peek();

            if (first < 0)
                throw new InvalidSourceFormatException("JSON", "неожиданный конец файла.", Line);

            if (first == '{' || first == '[')
                return ReadBalanced();

            if (first == '"')
                return ReadStringRaw();

            StringBuilder text = new StringBuilder();

            while (true)
            {
                int symbol = Peek();

                if (symbol < 0 || symbol == ',' || symbol == ']' || symbol == '}' ||
                    symbol == ' ' || symbol == '\t' || symbol == '\r' || symbol == '\n')
                {
                    break;
                }

                text.Append((char)Read());
            }

            if (text.Length == 0)
                throw new InvalidSourceFormatException("JSON", "ожидалось значение.", Line);

            return text.ToString();
        }

        private string ReadBalanced()
        {
            StringBuilder text = new StringBuilder();
            int depth = 0;
            bool insideString = false;
            bool escaped = false;

            while (true)
            {
                int symbol = Read();

                if (symbol < 0)
                    throw new InvalidSourceFormatException("JSON", "неожиданный конец файла.", Line);

                text.Append((char)symbol);

                if (insideString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (symbol == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (symbol == '"')
                        insideString = false;

                    continue;
                }

                if (symbol == '"')
                {
                    insideString = true;
                    continue;
                }

                if (symbol == '{' || symbol == '[')
                {
                    depth++;
                    continue;
                }

                if (symbol == '}' || symbol == ']')
                {
                    depth--;

                    if (depth < 0)
                        throw new InvalidSourceFormatException("JSON", "лишняя закрывающая скобка.", Line);

                    if (depth == 0)
                        return text.ToString();
                }
            }
        }

        private string ReadStringRaw()
        {
            StringBuilder text = new StringBuilder();
            text.Append('"');

            Read();

            bool escaped = false;

            while (true)
            {
                int symbol = Read();

                if (symbol < 0)
                    throw new InvalidSourceFormatException("JSON", "не закрыта строка.", Line);

                text.Append((char)symbol);

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (symbol == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (symbol == '"')
                    return text.ToString();
            }
        }

        private char ReadHexChar()
        {
            int value = 0;

            for (int i = 0; i < 4; i++)
            {
                int symbol = Read();
                int digit = HexDigit(symbol);

                if (digit < 0)
                    throw new InvalidSourceFormatException("JSON", "ожидались 4 шестнадцатеричные цифры.", Line);

                value = value * 16 + digit;
            }

            return (char)value;
        }

        private static int HexDigit(int symbol)
        {
            if (symbol >= '0' && symbol <= '9')
                return symbol - '0';

            if (symbol >= 'a' && symbol <= 'f')
                return symbol - 'a' + 10;

            if (symbol >= 'A' && symbol <= 'F')
                return symbol - 'A' + 10;

            return -1;
        }
    }
}