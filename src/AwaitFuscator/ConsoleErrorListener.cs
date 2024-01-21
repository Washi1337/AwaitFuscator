using AsmResolver;

namespace AwaitFuscator;

internal class ConsoleErrorListener : IErrorListener
{
    public void MarkAsFatal()
    {
    }

    public void RegisterException(Exception? exception)
    {
        string indent = "";
        while (exception is not null)
        {
            Console.Error.WriteLine($"[!] {indent}{exception.Message}");
            exception = exception.InnerException;
            indent += "  ";
        }
    }
}