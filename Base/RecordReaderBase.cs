using DataConverter.Exceptions;
using DataConverter.Models;

namespace DataConverter.Base;

public abstract class RecordReaderBase : IRecordReader
{
    public abstract string FormatName { get; }
 
    public IReadOnlyList<DataRecord> Read(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);
 
        try
        {
            return ReadCore(input);
        }
        catch (IOException ex)
        {
            throw new ReadException($"Ошибка чтения {FormatName}: {ex.Message}", ex);
        }
    }
    public abstract IReadOnlyList<DataRecord> ReadCore(TextReader input);
}