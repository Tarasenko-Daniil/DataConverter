using System.Xml;
using System.Xml.Schema;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;
 
namespace DataConverter.Xml;
 
public class XmlRecordReader : RecordReaderBase
{

    public const string AttributePrefix = "@";
    public const string TextKey = "#text";
 
    public const string ItemName = "item";
    public const string DefaultRootName = "root";
    public const string DefaultRecordName = "record";
 
    private string? _recordPath;
 
    public XmlRecordReader(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recordPath = options.RecordPath;
    }

    public override string FormatName
    {
        get
        {
            return "xml"; 
        }
    }

    public override IReadOnlyList<DataRecord> ReadCore(TextReader input)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            };
            
            var xmlReader = XmlReader.Create(input, settings); // using
 
            var document = new XmlDocument { XmlResolver = null };
            document.Load(xmlReader);
 
            var place = Locate(document.DocumentElement!);
            return ToRecords(place);
        }
        catch (XmlException ex)
        {
            int? line;
            if (ex.LineNumber > 0)
            {
                line = ex.LineNumber;
            }
            else
            {
                line = null;
            } 
            throw new InvalidSourceFormatException("XML", ex.Message, line, ex);
        }
    }
 

    private XmlElement Locate(XmlElement root)
    {
        if (string.IsNullOrWhiteSpace(_recordPath))
            return root;

        var current = root;

        foreach (var segment in _recordPath.Split('.'))
        {
            XmlElement? next = null;

            foreach (var child in ChildElements(current))
            {
                var name = XmlConvert.DecodeName(child.LocalName);

                if (name == segment)
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
                throw new RecordPathNotFoundException(_recordPath);

            current = next;
        }

        return current;
    }
    private static IReadOnlyList<DataRecord> ToRecords(XmlElement container)
    {
        if (!HasElements(container) && container.InnerText.Trim().Length > 0)
            throw new InvalidSourceFormatException("XML", $"элемент <{container.LocalName}> не содержит вложенных записей, только текст.");
 
        var records = new List<DataRecord>();
        var index = 0;
 
        foreach (var child in ChildElements(container))
        {
            index++;
            var converted = ConvertElement(child);
 
            if (converted is DataRecord record)
            {
                records.Add(record);
            }
            else if (converted is string { Length: 0 })
            {
                records.Add(new DataRecord());
            }
            else
            {
                throw new InvalidSourceFormatException("XML", $"элемент №{index} <{child.LocalName}> не является записью " + "(нет вложенных элементов и атрибутов). Возможно, нужно указать путь к записям.");
            }
        }
 
        return records;
    }
 
    private static object? ConvertElement(XmlElement element)
    {
        if (IsNil(element))
            return null;
 
        var attributes = element.Attributes.Cast<XmlAttribute>().Where(IsDataAttribute).ToList();
        var hasElements = HasElements(element);
 
        if (attributes.Count == 0 && !hasElements)
            return element.InnerText;
 
        var record = new DataRecord();
 
        foreach (var attribute in attributes)
            record.Set(AttributePrefix + XmlConvert.DecodeName(attribute.LocalName), attribute.Value);
 
        foreach (var child in ChildElements(element))
        {
            var name = XmlConvert.DecodeName(child.LocalName);
            var value = ConvertElement(child);
 
            if (!record.Contains(name))
            {
                record.Set(name, value);
            }
            else if (record.Get(name) is List<object?> list)
            {
                list.Add(value);
            }
            else
            {
                record.Set(name, new List<object?> { record.Get(name), value });
            }
        }
 
        var text = string.Concat(element.ChildNodes.Cast<XmlNode>()
            .Where(n => n.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            .Select(n => n.Value));
 
        if (hasElements)
            text = text.Trim();
 
        if (text.Length > 0)
            record.Set(TextKey, text);
 
        return record;
    }
 
    private static IEnumerable<XmlElement> ChildElements(XmlElement element)
    {
        return element.ChildNodes.OfType<XmlElement>();
    }
 
    private static bool HasElements(XmlElement element)
    {
        return element.ChildNodes.OfType<XmlElement>().Any();
    }
    private static bool IsNil(XmlElement element)
    {
        var nil = element.GetAttribute("nil", XmlSchema.InstanceNamespace);
        return nil is "true" or "1";
    }
 
    private static bool IsDataAttribute(XmlAttribute attribute)
    {
        var isNamespaceDeclaration = attribute.Prefix == "xmlns" || attribute.Name == "xmlns";
        return !isNamespaceDeclaration && attribute.NamespaceURI != XmlSchema.InstanceNamespace;
    }
}