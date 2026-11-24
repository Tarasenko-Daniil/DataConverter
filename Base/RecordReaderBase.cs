using DataConverter.Exceptions;
using DataConverter.Models;

namespace DataConverter.Base;

public abstract class RecordReaderBase : IRecordReader
{
    public abstract string FormatName { get; }

    public IEnumerable<DataRecord> Read(TextReader input)
    {
        ArgumentNullException.ThrowIfNull(input);

        IEnumerator<DataRecord> enumerator = ReadCore(input).GetEnumerator();

        try
        {
            while (true)
            {
                DataRecord current;

                try
                {
                    if (enumerator.MoveNext() == false)
                    {
                        yield break;
                    }

                    current = enumerator.Current;
                }
                catch (IOException ex)
                {
                    throw new ReadException($"Ошибка чтения {FormatName}: {ex.Message}", ex);
                }

                yield return current;
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    protected abstract IEnumerable<DataRecord> ReadCore(TextReader input);
}
