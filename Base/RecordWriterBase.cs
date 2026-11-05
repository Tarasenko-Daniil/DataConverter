using DataConverter.Exceptions;
using DataConverter.Models;

namespace DataConverter.Base;

public abstract class RecordWriterBase : IRecordWriter
{ 
    public abstract string FormatName { get; }
 
    public void Write(IReadOnlyList<DataRecord> records, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(output);
 
        try
        {
            WriteCore(records, output);
        }
        catch (IOException ex)
        {
            throw new WriteException($"Ошибка записи {FormatName}: {ex.Message}", ex);
        }
    }
 
    public abstract void WriteCore(IReadOnlyList<DataRecord> records, TextWriter output);
}