using DataConverter.Models;
 
namespace DataConverter;
public interface IRecordReader
{
    string FormatName { get; }
    IReadOnlyList<DataRecord> Read(TextReader input);
} 
public interface IRecordWriter
{
    string FormatName { get; }
    void Write(IReadOnlyList<DataRecord> records, TextWriter output);
}