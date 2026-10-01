namespace MobileSpeaker;

public static class Log
{
    private static readonly object Sync = new();

    public static void Info(string message) => Write(message, null);

    public static void Warn(string message) => Write(message, ConsoleColor.Yellow);

    public static void Error(string message) => Write(message, ConsoleColor.Red);

    private static void Write(string message, ConsoleColor? color)
    {
        lock (Sync)
        {
            if (color.HasValue)
                Console.ForegroundColor = color.Value;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (color.HasValue)
                Console.ResetColor();
        }
    }
}
