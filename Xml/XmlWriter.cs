
using System.Globalization;
using System.Xml;
using System.Xml.Schema;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;
 
namespace DataConverter.Xml;
 
public sealed class XmlRecordWriter : RecordWriterBase
{
    private readonly string _rootName;
    private readonly string _recordName;
 
    public XmlRecordWriter(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
 
        _rootName = string.IsNullOrWhiteSpace(options.RootName)
            ? XmlRecordReader.DefaultRootName
            : options.RootName;
 
        _recordName = string.IsNullOrWhiteSpace(options.RecordName)
            ? XmlRecordReader.DefaultRecordName
            : options.RecordName;
    }
 
    public override string FormatName
    {
        get
        {
            return "xml"; 
        }
    } 
    public override void WriteCore(IReadOnlyList<DataRecord> records, TextWriter output)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.Entitize
        };
 
        var buffer = new StringWriter(CultureInfo.InvariantCulture);
 
        try
        {
            buffer.Write($"<?xml version=\"1.0\" encoding=\"{output.Encoding.WebName}\"?>");
            buffer.Write(settings.NewLineChars);
 
            using (var writer = XmlWriter.Create(buffer, settings))
            {
                writer.WriteStartElement(EncodeName(_rootName));
                writer.WriteAttributeString("xmlns", "xsi", null, XmlSchema.InstanceNamespace);
 
                foreach (var record in records)
                {
                    WriteElement(writer, _recordName, record);
                }
 
                writer.WriteEndElement();
            }
 
            output.Write(buffer.ToString());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            throw new WriteException("Ошибка записи XML: " + ex.Message, ex);
        }
        finally
        {
            buffer.Dispose();
        }
    }
 
    private static void WriteElement(XmlWriter w, string name, object? value)
    {
        if (value is List<object?> list)
        {
            if (list.Count == 0)
            {
                w.WriteStartElement(EncodeName(name));
                w.WriteEndElement();
                return;
            }
 
            foreach (var item in list)
            {
                if (item is List<object?>)
                {
                    w.WriteStartElement(EncodeName(name));
                    WriteElement(w, XmlRecordReader.ItemName, item);
                    w.WriteEndElement();
                }
                else
                {
                    WriteElement(w, name, item);
                }
            }
 
            return;
        }
 
        w.WriteStartElement(EncodeName(name));
 
        switch (value)
        {
            case null:
                w.WriteAttributeString("xsi", "nil", XmlSchema.InstanceNamespace, "true");
                break;
 
            case DataRecord record:
                WriteContent(w, record);
                break;
 
            default:
                w.WriteString(ValueFormatter.Format(value));
                break;
        }
 
        w.WriteEndElement();
    }
 
    private static void WriteContent(XmlWriter w, DataRecord record)
    {
        var fields = record.Fields;
 
        foreach (var field in fields)
        {
            if (IsAttribute(field))
                w.WriteAttributeString(EncodeName(field.Key.Substring(1)), ValueFormatter.Format(field.Value));
        }
 
        foreach (var field in fields)
        {
            if (IsAttribute(field))
                continue;
 
            if (field.Key == XmlRecordReader.TextKey && IsScalar(field.Value))
            {
                w.WriteString(ValueFormatter.Format(field.Value));
                continue;
            }
            
            WriteElement(w, field.Key, field.Value);
        }
    }
 
    private static bool IsAttribute(KeyValuePair<string, object?> field)
    {
        return field.Key.Length > 1
               && field.Key.StartsWith(XmlRecordReader.AttributePrefix, StringComparison.Ordinal)
               && IsScalar(field.Value);
    }
 
    private static bool IsScalar(object? value)
    {
        return value is not null and not DataRecord and not List<object?>;
    }
 
    private static string EncodeName(string name)
    {
        return name.Length == 0 ? "_" : XmlConvert.EncodeLocalName(name);
    }
}