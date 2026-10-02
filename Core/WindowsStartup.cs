using Microsoft.Win32;

namespace MobileSpeaker.Core;

/// <summary>
/// "Iniciar con Windows": una entrada en HKCU\...\Run para el usuario actual.
/// Al iniciar asi, el programa arranca minimizado en la bandeja.
/// </summary>
public static class WindowsStartup
{
    public const string MinimizedArgument = "--minimized";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MobileSpeaker";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MobileSpeaker.exe");
                key.SetValue(ValueName, $"\"{exe}\" {MinimizedArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo cambiar el inicio con Windows: {ex.Message}");
            return false;
        }
    }
}
