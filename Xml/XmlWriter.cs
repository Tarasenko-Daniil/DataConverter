using System.Text;
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

        _rootName = string.IsNullOrWhiteSpace(options.RootName) ? XmlRecordReader.DefaultRootName : options.RootName;

        _recordName = string.IsNullOrWhiteSpace(options.RecordName)
            ? XmlRecordReader.DefaultRecordName
            : options.RecordName;
    }

    public override string FormatName
    {
        get { return "xml"; }
    }

    protected override int WriteCore(IEnumerable<DataRecord> records, Stream outputStream)
    {
        XmlWriterSettings settings = new XmlWriterSettings();
        settings.Indent = true;
        settings.IndentChars = "  ";
        settings.OmitXmlDeclaration = false;
        settings.NewLineHandling = NewLineHandling.Entitize;
        settings.Encoding = new UTF8Encoding(false);
        settings.CloseOutput = false;

        int count = 0;

        try
        {
            using (XmlWriter writer = XmlWriter.Create(outputStream, settings))
            {
                writer.WriteStartElement(EncodeName(_rootName));
                writer.WriteAttributeString("xmlns", "xsi", null, XmlSchema.InstanceNamespace);

                foreach (DataRecord record in records)
                {
                    WriteElement(writer, _recordName, record);
                    count++;
                }

                writer.WriteEndElement();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            throw new WriteException("Ошибка записи XML: " + ex.Message, ex);
        }

        return count;
    }

    private static void WriteElement(XmlWriter writer, string name, object? value)
    {
        List<object?>? list = value as List<object?>;

        if (list != null)
        {
            if (list.Count == 0)
            {
                writer.WriteStartElement(EncodeName(name));
                writer.WriteEndElement();
                return;
            }

            foreach (object? item in list)
            {
                if (item is List<object?>)
                {
                    writer.WriteStartElement(EncodeName(name));
                    WriteElement(writer, XmlRecordReader.ItemName, item);
                    writer.WriteEndElement();
                }
                else
                {
                    WriteElement(writer, name, item);
                }
            }

            return;
        }

        writer.WriteStartElement(EncodeName(name));

        if (value == null)
        {
            writer.WriteAttributeString("xsi", "nil", XmlSchema.InstanceNamespace, "true");
        }
        else if (value is DataRecord record)
        {
            WriteContent(writer, record);
        }
        else
        {
            writer.WriteString(ValueFormatter.Format(value));
        }

        writer.WriteEndElement();
    }

    private static void WriteContent(XmlWriter writer, DataRecord record)
    {
        List<KeyValuePair<string, object?>> fields = record.Fields;

        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (IsAttribute(field) == false)
                continue;

            string name = EncodeName(field.Key.Substring(1));
            writer.WriteAttributeString(name, ValueFormatter.Format(field.Value));
        }

        foreach (KeyValuePair<string, object?> field in fields)
        {
            if (IsAttribute(field))
                continue;

            if (field.Key == XmlRecordReader.TextKey && IsScalar(field.Value))
            {
                writer.WriteString(ValueFormatter.Format(field.Value));
                continue;
            }

            WriteElement(writer, field.Key, field.Value);
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
        return value != null && value is not DataRecord && value is not List<object?>;
    }

    private static string EncodeName(string name)
    {
        if (name.Length == 0)
            return "_";

        return XmlConvert.EncodeLocalName(name);
    }
}