namespace DataConverter.Exceptions;
 
public abstract class ConversionException : Exception
{
    public ConversionException(string message, Exception? inner) : base(message, inner) { }
}
public class ReadException : ConversionException
{
    public ReadException(string message, Exception? inner) : base(message, inner) { }
}
 
public class WriteException : ConversionException
{
    public WriteException(string message, Exception? inner) : base(message, inner) { }
}