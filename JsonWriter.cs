using System.Text.Encodings.Web;
using System.Text.Json;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;

namespace DataConverter.Json;

public sealed class JsonRecordWriter : RecordWriterBase
{
    private readonly string? _rootName;

    public JsonRecordWriter(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rootName = options.RootName;
    }

    public override string FormatName
    {
        get { return "json"; }
    }

    protected override int WriteCore(IEnumerable<DataRecord> records, Stream output)
    {
        JsonWriterOptions options = new JsonWriterOptions();
        options.Indented = true;

        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        int count = 0;

        const int FlushEvery = 1000;

        try
        {
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output, options))
            {
                bool wrap = string.IsNullOrWhiteSpace(_rootName) == false;

                if (wrap)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName(_rootName!);
                }

                writer.WriteStartArray();

                foreach (DataRecord record in records)
                {
                    WriteValue(writer, record);
                    count++;

                    if (count % FlushEvery == 0)
                        writer.Flush();
                }

                writer.WriteEndArray();

                if (wrap)
                    writer.WriteEndObject();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            throw new WriteException("Ошибка записи JSON: " + ex.Message, ex);
        }

        return count;
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        if (value is string text)
        {
            writer.WriteStringValue(text);
            return;
        }

        if (value is bool flag)
        {
            writer.WriteBooleanValue(flag);
            return;
        }

        if (value is int intValue)
        {
            writer.WriteNumberValue(intValue);
            return;
        }

        if (value is long longValue)
        {
            writer.WriteNumberValue(longValue);
            return;
        }

        if (value is decimal decimalValue)
        {
            writer.WriteNumberValue(decimalValue);
            return;
        }

        if (value is double doubleValue)
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }

        if (value is DateTime date)
        {
            writer.WriteStringValue(ValueFormatter.Format(date));
            return;
        }

        DataRecord? record = value as DataRecord;

        if (record != null)
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, object?> field in record.Fields)
            {
                writer.WritePropertyName(field.Key);
                WriteValue(writer, field.Value);
            }

            writer.WriteEndObject();
            return;
        }

        List<object?>? list = value as List<object?>;

        if (list != null)
        {
            writer.WriteStartArray();

            foreach (object? item in list)
            {
                WriteValue(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        writer.WriteStringValue(ValueFormatter.Format(value));
    }
}