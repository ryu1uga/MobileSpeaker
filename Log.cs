namespace MobileSpeaker;

public enum LogKind
{
    Info,
    Warn,
    Error
}

/// <summary>
/// Registro simple basado en eventos. La interfaz se suscribe a <see cref="Message"/>.
/// </summary>
public static class Log
{
    public static event Action<LogKind, string>? Message;

    public static void Info(string message) => Raise(LogKind.Info, message);

    public static void Warn(string message) => Raise(LogKind.Warn, message);

    public static void Error(string message) => Raise(LogKind.Error, message);

    private static void Raise(LogKind level, string message)
    {
        try
        {
            Message?.Invoke(level, $"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        catch
        {
            // El registro nunca debe tumbar el programa.
        }
    }
}
