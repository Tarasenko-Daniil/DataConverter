using DataConverter.Exceptions;
using DataConverter.Models;

namespace DataConverter.Base;

public abstract class RecordWriterBase : IRecordWriter
{
    public abstract string FormatName { get; }

    public int Write(IEnumerable<DataRecord> records, Stream output)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            return WriteCore(records, output);
        }
        catch (IOException ex)
        {
            throw new WriteException($"Ошибка записи {FormatName}: {ex.Message}", ex);
        }
    }

    protected abstract int WriteCore(IEnumerable<DataRecord> records, Stream output);
}
