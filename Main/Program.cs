namespace DataConverter.Main;

using System.Text;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;
using DataConverter.UI;
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
            return new Consoleui().Run();

        try
        {
            switch (args[0])
            {
                case "convert": return Convert(Args.Parse(args[1..]));
                case "roundtrip": return RoundTripCommand(Args.Parse(args[1..]));
                case "-h":
                case "--help":
                    Usage();
                    return 0;
                default:
                    return Fail($"Неизвестная команда '{args[0]}'.");
            }
        }
        catch (ConversionException ex)
        {
            Console.Error.WriteLine("Ошибка конвертации: " + ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Ошибка: " + ex.Message);
            return 2;
        }
    }

    private static int Convert(Args a)
    {
        if (a.Positional.Count != 2)
            return Fail("convert: нужно указать <вход> и <выход>.");

        var input = a.Positional[0];
        var output = a.Positional[1];
        var from = a.Get("from") ?? Detect(input);
        var to = a.Get("to") ?? Detect(output);
        var options = a.ToOptions();

        IReadOnlyList<DataRecord> records;
        using (var reader = new StreamReader(input, Encoding.UTF8))
            records = ConverterFactory.CreateReader(from, options).Read(reader);

        using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            ConverterFactory.CreateWriter(to, options).Write(records, writer);

        Console.WriteLine($"{from} -> {to}: записей {records.Count}, результат: {output}");
        return 0;
    }

    private static int RoundTripCommand(Args a)
    {
        if (a.Positional.Count != 1 || a.Get("via") is null)
            return Fail("roundtrip: нужно <файл> --via <json|xml|csv>.");

        var path = a.Positional[0];
        var from = a.Get("from") ?? Detect(path);
        var options = a.ToOptions();

        IReadOnlyList<DataRecord> records;
        using (var reader = new StreamReader(path, Encoding.UTF8))
            records = ConverterFactory.CreateReader(from, options).Read(reader);

        var mid = new FormatOptions { Delimiter = options.Delimiter };
        Report(records, from, a.Get("via")!, mid, showText: a.Flags.ContainsKey("show"));
        return 0;
    }

    internal static void Report(IReadOnlyList<DataRecord> original, string a, string via, FormatOptions options, bool showText)
    {
        var viaText = ConverterFactory.WriteString(via, original, options);
        var viaRecords = ConverterFactory.ReadString(via, viaText, options);
        var backText = ConverterFactory.WriteString(a, viaRecords, options);
        var back = ConverterFactory.ReadString(a, backText, options);

        Console.WriteLine($"==== {a} -> {via} -> {a} ====");

        if (showText)
        {
            Console.WriteLine($"--- промежуточный {via}:\n{viaText}");
            Console.WriteLine($"--- обратно в {a}:\n{backText}");
        }

        var before = RecordDump.Lines(original, typed: true);
        var after = RecordDump.Lines(back, typed: true);

        if (before.SequenceEqual(after))
        {
            Console.WriteLine("OK: без потерь.\n");
            return;
        }

        if (RecordDump.Lines(original, typed: false).SequenceEqual(RecordDump.Lines(back, typed: false)))
        {
            Console.WriteLine("Структура сохранилась, потеряны только типы (числа/bool стали строками):");
        }
        else
        {
            Console.WriteLine("ДАННЫЕ ИЗМЕНИЛИСЬ:");
        }

        foreach (var line in RecordDump.Diff(before, after))
            Console.WriteLine("  " + line);
        Console.WriteLine();
    }

    internal static string Detect(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".json" => "json",
            ".xml" => "xml",
            ".csv" => "csv",
            _ => throw new ArgumentException($"Не могу определить формат по расширению '{ext}', укажи --from / --to.")
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Usage();
        return 1;
    }

    private static void Usage()
    {
        Console.WriteLine("""
            Использование:
              convert   <вход> <выход> [--from json|xml|csv] [--to json|xml|csv] [опции]
              roundtrip <файл> --via json|xml|csv [--show] [опции]   (A -> B -> A и сравнение)
              (без аргументов)                                        интерактивное меню

            Опции:
              --record-path a.b   путь к записям (JSON, XML)
              --root name         корневой элемент/ключ при записи (JSON, XML)
              --record name       имя элемента записи (XML)
              --delimiter ;       разделитель CSV (или 'tab')
              --sanitize          защита CSV от формул
            """);
    }

    private sealed class Args
    {
        public List<string> Positional { get; } = new();
        public Dictionary<string, string> Flags { get; } = new(StringComparer.Ordinal);
        public bool Sanitize { get; private set; }

        public static Args Parse(string[] raw)
        {
            var result = new Args();

            for (var i = 0; i < raw.Length; i++)
            {
                if (!raw[i].StartsWith("--", StringComparison.Ordinal))
                {
                    result.Positional.Add(raw[i]);
                    continue;
                }

                var name = raw[i][2..];

                if (name is "sanitize")
                {
                    result.Sanitize = true;
                    continue;
                }

                if (name is "show")
                {
                    result.Flags[name] = "1";
                    continue;
                }

                if (i + 1 >= raw.Length)
                    throw new ArgumentException($"У параметра --{name} нет значения.");

                result.Flags[name] = raw[++i];
            }

            return result;
        }

        public string? Get(string name) => Flags.GetValueOrDefault(name);

        public FormatOptions ToOptions()
        {
            var delimiter = ',';
            var d = Get("delimiter");

            if (d is not null)
                delimiter = d == "tab" ? '\t' : d.Length == 1 ? d[0] : throw new ArgumentException("--delimiter: один символ или 'tab'.");

            return new FormatOptions
            {
                RecordPath = Get("record-path"),
                RootName = Get("root"),
                RecordName = Get("record"),
                Delimiter = delimiter,
                SanitizeFormulas = Sanitize
            };
        }
    }
}