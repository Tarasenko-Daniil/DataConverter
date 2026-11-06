using System.Text.Json;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;

namespace DataConverter.Json;
 
public class JsonRecordReader : RecordReaderBase
{
    private string? _recordPath;
 
    public JsonRecordReader(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recordPath = options.RecordPath;
    }
 
    public override string FormatName
    {
        get
        {
            return "json";
        }
    } 
    public override IReadOnlyList<DataRecord> ReadCore(TextReader input)
    {
        var text = input.ReadToEnd();
 
        try
        {
            var options = new JsonDocumentOptions();
            options.CommentHandling = JsonCommentHandling.Skip;
            options.AllowTrailingCommas = false;

             var document = JsonDocument.Parse(text, options);
             var root = document.RootElement;
             var place = Locate(root);
             var records = ToRecords(place);
             return records;
        }
        catch (JsonException ex)
        {
            int? line;

            if (ex.LineNumber is long l)
            {
                line = (int)l + 1;
            }
            else
            {
                line = null;
            }            throw new InvalidSourceFormatException("JSON", ex.Message, line, ex);
        }
    }
 
    private JsonElement Locate(JsonElement root)
    {
        if (string.IsNullOrWhiteSpace(_recordPath))
            return root;
 
        var current = root;
        foreach (var segment in _recordPath.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                throw new RecordPathNotFoundException(_recordPath);
            current = next;
        }
 
        return current;
    }
 
    private static IReadOnlyList<DataRecord> ToRecords(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                var records = new List<DataRecord>(element.GetArrayLength());
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        throw new InvalidSourceFormatException("JSON", $"элемент массива №{index + 1} не является объектом ({item.ValueKind}).");
                    var converted = ConvertValue(item);
                    if (converted is DataRecord dataRecord)
                    {
                        records.Add(dataRecord);
                    }                    
                    index++;
                }
                return records;
 
            case JsonValueKind.Object:
                var convert = ConvertValue(element);

                if (convert is DataRecord record)
                {
                    var result = new List<DataRecord>();
                    result.Add(record);
                    return result;
                }

                throw new InvalidSourceFormatException("JSON", "не удалось разобрать объект.");
 
            default:
                throw new InvalidSourceFormatException("JSON", $"ожидался массив объектов или объект, получено: {element.ValueKind}.");
        }
    }
 
    private static object? ConvertValue(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var record = new DataRecord();
                foreach (var property in e.EnumerateObject())
                    record.Set(property.Name, ConvertValue(property.Value));
                return record;
 
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in e.EnumerateArray())
                    list.Add(ConvertValue(item));
                return list;
 
            case JsonValueKind.String: return e.GetString();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
 
            case JsonValueKind.Number:
                if (e.TryGetInt64(out var l))
                {
                    return l;
                }
                if (e.TryGetDecimal(out var d))
                {
                    return d;
                }
                return e.GetDouble();
 
            default: return null;
        }
    }
}