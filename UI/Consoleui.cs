namespace DataConverter.UI;

using System.Text;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;
using DataConverter.Main;

/// <summary>Интерактивное консольное меню (запускается, если программа вызвана без аргументов).</summary>
internal sealed class Consoleui
{
    private const int PreviewLines = 40;
    private const int DumpLimit = 200;

    public int Run()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("=== DataConverter: JSON <-> XML <-> CSV ===");

        while (true)
        {
            PrintMenu();
            var choice = Ask("Выбор");

            if (choice is null)
                return 0;

            try
            {
                switch (choice.Trim().ToLowerInvariant())
                {
                    case "1": ConvertFile(); break;
                    case "2": RoundTrip(); break;
                    case "3": ViewFile(); break;
                    case "0":
                    case "q":
                        return 0;
                    default:
                        Warn("Нет такого пункта меню.");
                        break;
                }
            }
            catch (ConversionException ex)
            {
                Warn("Ошибка конвертации: " + ex.Message);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Warn("Ошибка: " + ex.Message);
            }
        }
    }

    private void ConvertFile()
    {
        var input = PickFile("Входной файл");
        if (input is null) return;

        var from = ResolveFormat(input, "Формат входного файла");
        var to = AskFormat("Выходной формат", from == "json" ? "xml" : "json");

        var defaultOut = Path.Combine("output", Path.GetFileNameWithoutExtension(input) + "." + to);
        var output = Ask($"Выходной файл [{defaultOut}]");
        output = string.IsNullOrWhiteSpace(output) ? defaultOut : Unquote(output);

        if (File.Exists(output) && !Confirm($"Файл '{output}' уже существует. Перезаписать?", false))
        {
            Console.WriteLine("Отменено.");
            return;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var options = new FormatOptions();

        IReadOnlyList<DataRecord> records;
        using (var reader = new StreamReader(input, Encoding.UTF8))
            records = ConverterFactory.CreateReader(from, options).Read(reader);

        using (var writer = new StreamWriter(output, false, new UTF8Encoding(false)))
            ConverterFactory.CreateWriter(to, options).Write(records, writer);

        Console.WriteLine($"Готово: {from} -> {to}, записей: {records.Count}, файл: {Path.GetFullPath(output)}");

        if (Confirm("Показать результат?", true))
            Preview(output);
    }

    private void RoundTrip()
    {
        var input = PickFile("Входной файл");
        if (input is null) return;

        var from = ResolveFormat(input, "Формат входного файла");
        var via = AskFormat("Промежуточный формат", from == "json" ? "xml" : "json");
        var show = Confirm("Показывать промежуточные тексты?", false);

        var options = new FormatOptions();

        IReadOnlyList<DataRecord> records;
        using (var reader = new StreamReader(input, Encoding.UTF8))
            records = ConverterFactory.CreateReader(from, options).Read(reader);

        Console.WriteLine();
        Program.Report(records, from, via, options, show);
    }

    private void ViewFile()
    {
        var input = PickFile("Файл для просмотра");
        if (input is null) return;

        var from = ResolveFormat(input, "Формат файла");

        IReadOnlyList<DataRecord> records;
        using (var reader = new StreamReader(input, Encoding.UTF8))
            records = ConverterFactory.CreateReader(from, new FormatOptions()).Read(reader);

        Console.WriteLine($"Записей: {records.Count}");

        var lines = RecordDump.Lines(records, typed: true);
        foreach (var line in lines.Take(DumpLimit))
            Console.WriteLine("  " + line);

        if (lines.Count > DumpLimit)
            Console.WriteLine($"  ... ещё {lines.Count - DumpLimit} строк");
    }

    private static void PrintMenu()
    {
        Console.WriteLine();
        Console.WriteLine("  1) Конвертировать файл");
        Console.WriteLine("  2) Проверить round-trip (A -> B -> A)");
        Console.WriteLine("  3) Посмотреть содержимое файла");
        Console.WriteLine("  0) Выход");
    }

    private static string? PickFile(string prompt)
    {
        var samples = FindSamples();

        if (samples.Count > 0)
        {
            Console.WriteLine("Доступные примеры:");
            for (var i = 0; i < samples.Count; i++)
                Console.WriteLine($"  {i + 1}) {Path.GetFileName(samples[i])}");
        }

        while (true)
        {
            var answer = Ask($"{prompt} (номер или путь, Enter = отмена)");

            if (string.IsNullOrWhiteSpace(answer))
                return null;

            answer = Unquote(answer);

            if (int.TryParse(answer, out var n) && n >= 1 && n <= samples.Count)
                return samples[n - 1];

            if (File.Exists(answer))
                return answer;

            Warn($"Файл '{answer}' не найден.");
        }
    }

    private static List<string> FindSamples()
    {
        var result = new List<string>();
        var dirs = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "samples"),
            Path.Combine(Directory.GetCurrentDirectory(), "samples")
        };

        foreach (var dir in dirs.Distinct())
        {
            if (!Directory.Exists(dir))
                continue;

            result.AddRange(Directory.GetFiles(dir)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".json" or ".xml" or ".csv")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }

        return result.Select(Path.GetFullPath).Distinct().ToList();
    }

    private static string ResolveFormat(string path, string prompt)
    {
        try
        {
            return Program.Detect(path);
        }
        catch (ArgumentException)
        {
            return AskFormat(prompt, "json");
        }
    }

    private static string AskFormat(string prompt, string defaultFormat)
    {
        var list = string.Join("/", ConverterFactory.Formats);

        while (true)
        {
            var answer = Ask($"{prompt} ({list}) [{defaultFormat}]");
            var format = string.IsNullOrWhiteSpace(answer) ? defaultFormat : answer.Trim().ToLowerInvariant();

            if (ConverterFactory.Formats.Contains(format))
                return format;

            Warn($"Неизвестный формат '{format}'.");
        }
    }

    private static void Preview(string path)
    {
        var lines = File.ReadLines(path, Encoding.UTF8).Take(PreviewLines + 1).ToList();
        Console.WriteLine("--------");

        foreach (var line in lines.Take(PreviewLines))
            Console.WriteLine(line);

        if (lines.Count > PreviewLines)
            Console.WriteLine($"... (показаны первые {PreviewLines} строк)");

        Console.WriteLine("--------");
    }

    private static bool Confirm(string question, bool defaultYes)
    {
        var answer = Ask($"{question} [{(defaultYes ? "Y/n" : "y/N")}]");

        if (string.IsNullOrWhiteSpace(answer))
            return defaultYes;

        return answer.Trim().ToLowerInvariant() is "y" or "yes" or "д" or "да";
    }

    private static string? Ask(string prompt)
    {
        Console.Write(prompt + ": ");
        return Console.ReadLine();
    }

    private static void Warn(string message)
    {
        Console.WriteLine("! " + message);
    }

    private static string Unquote(string s) => s.Trim().Trim('"');
}