using DataConverter.Formats;
using DataConverter.Models;

namespace DataConverter;

public static class RecordDump
{
    public static List<string> Lines(IReadOnlyList<DataRecord> records, bool typed)
    {
        var result = new List<string>();
        for (var i = 0; i < records.Count; i++)
            Walk($"[{i}]", records[i], result, typed);
        return result;
    }

    public static List<string> Diff(List<string> before, List<string> after)
    {
        var diff = new List<string>();
        foreach (var line in before.Except(after)) diff.Add("- " + line);
        foreach (var line in after.Except(before)) diff.Add("+ " + line);
        return diff;
    }

    private static void Walk(string path, object? value, List<string> output, bool typed)
    {
        switch (value)
        {
            case null:
                output.Add($"{path} = null");
                break;

            case DataRecord record:
                if (record.Count == 0)
                {
                    output.Add($"{path} = {{}}");
                    break;
                }
                foreach (var field in record.Fields)
                    Walk($"{path}/{field.Key}", field.Value, output, typed);
                break;

            case List<object?> list:
                if (list.Count == 0)
                {
                    output.Add($"{path} = []");
                    break;
                }
                for (var i = 0; i < list.Count; i++)
                    Walk($"{path}[{i}]", list[i], output, typed);
                break;

            case string s:
                output.Add($"{path} = {Quote(s)}");
                break;

            default:
                var text = ValueFormatter.Format(value);
                output.Add(typed
                    ? $"{path} = {value.GetType().Name}:{text}"
                    : $"{path} = {Quote(text)}");
                break;
        }
    }

    private static string Quote(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}