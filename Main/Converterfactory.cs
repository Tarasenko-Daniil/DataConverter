namespace DataConverter.Main;

using System.Globalization;
using System.Text;
using DataConverter.Csv;
using DataConverter.Formats;
using DataConverter.Json;
using DataConverter.Models;
using DataConverter.Xml;
public static class ConverterFactory
{
    public static readonly string[] Formats = { "json", "xml", "csv" };

    public static IRecordReader CreateReader(string format, FormatOptions options)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => new JsonRecordReader(options),
            "xml" => new XmlRecordReader(options),
            "csv" => new CsvRecordReader(options),
            _ => throw new ArgumentException($"Неизвестный формат: '{format}'.")
        };
    }

    public static IRecordWriter CreateWriter(string format, FormatOptions options)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => new JsonRecordWriter(options),
            "xml" => new XmlRecordWriter(options),
            "csv" => new CsvRecordWriter(options),
            _ => throw new ArgumentException($"Неизвестный формат: '{format}'.")
        };
    }

    public static IReadOnlyList<DataRecord> ReadString(string format, string text, FormatOptions? options = null)
    {
        using var reader = new StringReader(text);
        return CreateReader(format, options ?? new FormatOptions()).Read(reader);
    }

    public static string WriteString(string format, IReadOnlyList<DataRecord> records, FormatOptions? options = null)
    {
        using var writer = new Utf8StringWriter();
        CreateWriter(format, options ?? new FormatOptions()).Write(records, writer);
        return writer.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter() : base(CultureInfo.InvariantCulture) { }
        public override Encoding Encoding => Encoding.UTF8;
    }
}