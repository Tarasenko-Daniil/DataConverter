using System.Text;
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
 
    public override string FormatName => "json";
 
    public override void WriteCore(IReadOnlyList<DataRecord> records, TextWriter output)
    {
        var options = new JsonWriterOptions();
        options.Indented = true;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
 
        var buffer = new MemoryStream();
        var writer = new Utf8JsonWriter(buffer, options);
 
        try
        {
            var wrap = !string.IsNullOrWhiteSpace(_rootName);
 
            if (wrap)
            {
                writer.WriteStartObject();
                writer.WritePropertyName(_rootName!);
            }
 
            writer.WriteStartArray();
            foreach (var record in records)
            {
                WriteValue(writer, record);
            }
            writer.WriteEndArray();
 
            if (wrap)
            {
                writer.WriteEndObject();
            }
 
            writer.Flush();
            output.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            throw new WriteException("Ошибка записи JSON: " + ex.Message, ex);
        }
        finally
        {
            writer.Dispose();
            buffer.Dispose();
        }
    }
 
    private static void WriteValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case decimal d: w.WriteNumberValue(d); break;
            case double db: w.WriteNumberValue(db); break;
            case DateTime dt: w.WriteStringValue(ValueFormatter.Format(dt)); break;
 
            case DataRecord record:
                w.WriteStartObject();
                foreach (var field in record.Fields)
                {
                    w.WritePropertyName(field.Key);
                    WriteValue(w, field.Value);
                }
                w.WriteEndObject();
                break;
 
            case List<object?> list:
                w.WriteStartArray();
                foreach (var item in list)
                {
                    WriteValue(w, item);
                }
                w.WriteEndArray();
                break;
 
            default: w.WriteStringValue(ValueFormatter.Format(value)); break;
        }
    }
}