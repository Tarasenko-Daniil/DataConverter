using System.Xml;
using System.Xml.Schema;
using DataConverter.Base;
using DataConverter.Exceptions;
using DataConverter.Formats;
using DataConverter.Models;

namespace DataConverter.Xml;

public sealed class XmlRecordReader : RecordReaderBase
{
    public const string AttributePrefix = "@";
    public const string TextKey = "#text";

    public const string ItemName = "item";
    public const string DefaultRootName = "root";
    public const string DefaultRecordName = "record";

    private readonly string? _recordPath;

    public XmlRecordReader(FormatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recordPath = options.RecordPath;
    }

    public override string FormatName
    {
        get { return "xml"; }
    }

    protected override IEnumerable<DataRecord> ReadCore(TextReader input)
    {
        XmlReaderSettings settings = new XmlReaderSettings();
        settings.DtdProcessing = DtdProcessing.Prohibit;
        settings.XmlResolver = null;
        settings.IgnoreComments = true;
        settings.IgnoreProcessingInstructions = true;
        settings.IgnoreWhitespace = true;

        using XmlReader reader = XmlReader.Create(input, settings);

        if (MoveToContent(reader) == false)
            throw new InvalidSourceFormatException("XML", "документ пуст.");

        if (reader.NodeType != XmlNodeType.Element)
            throw new InvalidSourceFormatException("XML", "нет корневого элемента.");

        bool hasPath = string.IsNullOrWhiteSpace(_recordPath) == false;
        int containerDepth = reader.Depth;

        if (hasPath)
        {
            foreach (string segment in _recordPath!.Split('.'))
            {
                if (FindChildElement(reader, segment, containerDepth) == false)
                    throw new RecordPathNotFoundException(_recordPath);

                containerDepth = reader.Depth;
            }
        }

        string containerName = XmlConvert.DecodeName(reader.LocalName);
        List<KeyValuePair<string, string>> containerAttributes = ReadDataAttributes(reader);

        int childDepth = containerDepth + 1;
        int childrenCount = 0;
        bool decided = false;
        bool childrenAreRecords = false;
        DataRecord? singleRecord = null;

        while (ReadNext(reader))
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == containerDepth)
                break;

            if (reader.NodeType != XmlNodeType.Element || reader.Depth != childDepth)
                continue;

            childrenCount++;

            XmlElement child = LoadElement(reader);
            string childName = XmlConvert.DecodeName(child.LocalName);

            if (decided == false)
            {
                decided = true;
                childrenAreRecords = hasPath || LooksLikeRecord(child);

                if (childrenAreRecords == false)
                {
                    singleRecord = new DataRecord();

                    foreach (KeyValuePair<string, string> attribute in containerAttributes)
                        singleRecord.Set(AttributePrefix + attribute.Key, attribute.Value);
                }
            }

            if (childrenAreRecords)
            {
                yield return ToRecord(child, childrenCount);
            }
            else
            {
                AddField(singleRecord!, childName, ConvertElement(child));
            }
        }

        if (childrenCount == 0)
        {
            throw new InvalidSourceFormatException("XML",
                $"элемент <{containerName}> не содержит вложенных записей.");
        }

        if (childrenAreRecords == false)
            yield return singleRecord!;
    }

    private static bool FindChildElement(XmlReader reader, string name, int parentDepth)
    {
        if (reader.IsEmptyElement)
            return false;

        while (ReadNext(reader))
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == parentDepth)
                return false;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.Depth != parentDepth + 1)
                continue;

            string found = XmlConvert.DecodeName(reader.LocalName);

            if (found == name)
                return true;
        }

        return false;
    }

    private static XmlElement LoadElement(XmlReader reader)
    {
        try
        {
            using XmlReader subtree = reader.ReadSubtree();

            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.Load(subtree);

            return document.DocumentElement!;
        }
        catch (XmlException ex)
        {
            throw ToFormatException(ex);
        }
    }

    private static bool ReadNext(XmlReader reader)
    {
        try
        {
            return reader.Read();
        }
        catch (XmlException ex)
        {
            throw ToFormatException(ex);
        }
    }

    private static bool MoveToContent(XmlReader reader)
    {
        try
        {
            return reader.MoveToContent() != XmlNodeType.None;
        }
        catch (XmlException ex)
        {
            throw ToFormatException(ex);
        }
    }

    private static InvalidSourceFormatException ToFormatException(XmlException ex)
    {
        int? line = null;

        if (ex.LineNumber > 0)
            line = ex.LineNumber;

        return new InvalidSourceFormatException("XML", ex.Message, line, ex);
    }

    private static List<KeyValuePair<string, string>> ReadDataAttributes(XmlReader reader)
    {
        List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();

        if (reader.HasAttributes == false)
            return result;

        while (reader.MoveToNextAttribute())
        {
            if (IsDataAttribute(reader) == false)
                continue;

            string name = XmlConvert.DecodeName(reader.LocalName);
            result.Add(new KeyValuePair<string, string>(name, reader.Value));
        }

        reader.MoveToElement();
        return result;
    }

    private static bool LooksLikeRecord(XmlElement element)
    {
        if (HasElements(element))
            return true;

        foreach (XmlAttribute attribute in element.Attributes)
        {
            if (IsDataAttribute(attribute))
                return true;
        }

        return false;
    }

    private static DataRecord ToRecord(XmlElement element, int index)
    {
        object? value = ConvertElement(element);
        DataRecord? record = value as DataRecord;

        if (record != null)
            return record;

        string? text = value as string;

        if (text != null && text.Length == 0)
            return new DataRecord();

        throw new InvalidSourceFormatException("XML",
            $"элемент №{index} <{element.LocalName}> не является записью " +
            "(нет вложенных элементов и атрибутов). Возможно, нужно указать путь к записям.");
    }

    private static void AddField(DataRecord record, string name, object? value)
    {
        if (record.Contains(name) == false)
        {
            record.Set(name, value);
            return;
        }

        object? existing = record.Get(name);
        List<object?>? list = existing as List<object?>;

        if (list != null)
        {
            list.Add(value);
            return;
        }

        List<object?> newList = new List<object?>();
        newList.Add(existing);
        newList.Add(value);
        record.Set(name, newList);
    }

    private static object? ConvertElement(XmlElement element)
    {
        if (IsNil(element))
            return null;

        List<XmlAttribute> attributes = new List<XmlAttribute>();

        foreach (XmlAttribute attribute in element.Attributes)
        {
            if (IsDataAttribute(attribute))
                attributes.Add(attribute);
        }

        bool hasElements = HasElements(element);

        if (attributes.Count == 0 && hasElements == false)
            return element.InnerText;

        DataRecord record = new DataRecord();

        foreach (XmlAttribute attribute in attributes)
        {
            string name = XmlConvert.DecodeName(attribute.LocalName);
            record.Set(AttributePrefix + name, attribute.Value);
        }

        foreach (XmlElement child in ChildElements(element))
        {
            string name = XmlConvert.DecodeName(child.LocalName);
            AddField(record, name, ConvertElement(child));
        }

        string text = ReadOwnText(element);

        if (hasElements)
            text = text.Trim();

        if (text.Length > 0)
            record.Set(TextKey, text);

        return record;
    }

    private static string ReadOwnText(XmlElement element)
    {
        string text = string.Empty;

        foreach (XmlNode node in element.ChildNodes)
        {
            if (node.NodeType == XmlNodeType.Text || node.NodeType == XmlNodeType.CDATA)
                text = text + node.Value;
        }

        return text;
    }

    private static List<XmlElement> ChildElements(XmlElement element)
    {
        List<XmlElement> result = new List<XmlElement>();

        foreach (XmlNode node in element.ChildNodes)
        {
            XmlElement? child = node as XmlElement;

            if (child != null)
                result.Add(child);
        }

        return result;
    }

    private static bool HasElements(XmlElement element)
    {
        foreach (XmlNode node in element.ChildNodes)
        {
            if (node is XmlElement)
                return true;
        }

        return false;
    }

    private static bool IsNil(XmlElement element)
    {
        string nil = element.GetAttribute("nil", XmlSchema.InstanceNamespace);
        return nil == "true" || nil == "1";
    }

    private static bool IsDataAttribute(XmlAttribute attribute)
    {
        bool isNamespaceDeclaration = attribute.Prefix == "xmlns" || attribute.Name == "xmlns";
        return isNamespaceDeclaration == false && attribute.NamespaceURI != XmlSchema.InstanceNamespace;
    }

    private static bool IsDataAttribute(XmlReader reader)
    {
        bool isNamespaceDeclaration = reader.Prefix == "xmlns" || reader.Name == "xmlns";
        return isNamespaceDeclaration == false && reader.NamespaceURI != XmlSchema.InstanceNamespace;
    }
}