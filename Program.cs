using System.Windows.Forms;
using MobileSpeaker.Core;
using MobileSpeaker.UI;

namespace MobileSpeaker;

internal static class Program
{
    // El instalador usa el mismo nombre (AppMutex) para saber si el programa esta abierto.
    private const string MutexName = "MobileSpeakerSingleInstance";
    private const string ShowEventName = "MobileSpeakerShowWindow";

    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Ya hay una instancia abierta: se le pide que muestre su ventana.
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // La otra instancia todavia esta arrancando.
            }
            return 0;
        }

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowFatal(ex);
        };

        bool startHidden = args.Any(a => string.Equals(a, WindowsStartup.MinimizedArgument, StringComparison.OrdinalIgnoreCase));

        using (var form = new MainForm(startHidden, showEvent))
        {
            Application.Run(form);
        }

        try { mutex.ReleaseMutex(); } catch { }
        return 0;
    }

    private static void ShowFatal(Exception ex)
    {
        try
        {
            Log.Error(ex.Message);
            MessageBox.Show($"Ocurrio un error inesperado:\n\n{ex.Message}", "MobileSpeaker",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}
