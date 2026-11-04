using System.Globalization;
 
namespace DataConverter.Formats;
 
public class FormatOptions
{
    public string? RecordPath { get;}
    public string? RootName { get;}
}
 
public class ValueFormatter
{
    public static string Format(object? value)
    {
        if (value is null)
            return string.Empty;
 
        if (value is string text)
            return text;
 
        if (value is bool flag)
        {
            if (flag)
                return "true";
 
            return "false";
        }
 
        if (value is DateTime date)
        {
            if (date.TimeOfDay == TimeSpan.Zero)
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
 
            return date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }
 
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        var result = value.ToString();

        if (result is null)
        {
            return string.Empty;
        }

        return result;
    }
}