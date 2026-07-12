namespace Menuki.Actions;

public class ErrorActionExecutor : IActionExecutor
{
    private readonly string _message;

    public ErrorActionExecutor(string message)
    {
        _message = message;
    }

    public string? Execute()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(_message);
        Console.ResetColor();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press any key to continue...");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
        return null;
    }
}
