using System.Net;
using System.Reflection;
using MobileSpeaker.Audio;
using MobileSpeaker.Server;

namespace MobileSpeaker.Core;

public enum BridgeState
{
    Stopped,
    Starting,
    Running,
    Stopping
}

/// <summary>
/// El "puente": captura de audio + servidor web. Se puede activar y desactivar las veces que se quiera.
/// </summary>
public sealed class BridgeService : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ClientHub _hub = new();
    private readonly byte[] _indexHtml;
    private AudioEngine? _audio;
    private WebApplication? _app;

    public BridgeService()
    {
        _indexHtml = LoadIndexHtml();
        _hub.ClientsChanged += count => ClientsChanged?.Invoke(count);
    }

    public BridgeState State { get; private set; } = BridgeState.Stopped;
    public int Port { get; private set; }
    public int ClientCount => _hub.Count;
    public string DeviceName => _audio?.DeviceName ?? string.Empty;
    public string SourceDescription => _audio?.SourceDescription ?? string.Empty;
    public int SampleRate => _audio?.SampleRate ?? 0;

    /// <summary>Cambio de estado. Puede llegar desde cualquier hilo.</summary>
    public event Action<BridgeState>? StateChanged;

    /// <summary>Cambio de dispositivo o formato de audio. Puede llegar desde cualquier hilo.</summary>
    public event Action? AudioChanged;

    /// <summary>Cantidad de celulares conectados. Puede llegar desde cualquier hilo.</summary>
    public event Action<int>? ClientsChanged;

    public async Task StartAsync(int port)
    {
        await _gate.WaitAsync();
        try
        {
            if (State != BridgeState.Stopped)
                return;

            SetState(BridgeState.Starting);

            var audio = new AudioEngine { HasListeners = () => _hub.Count > 0 };
            audio.BlockReady += _hub.Broadcast;
            audio.FormatChanged += OnAudioFormatChanged;

            WebApplication? app = null;
            try
            {
                // La captura se inicia fuera del hilo de la interfaz.
                await Task.Run(audio.Start);

                app = BuildWebApp(port, () => audio.SampleRate);
                await app.StartAsync();
            }
            catch (Exception ex)
            {
                audio.Dispose();
                if (app is not null)
                    await app.DisposeAsync();

                SetState(BridgeState.Stopped);
                throw new BridgeStartException(DescribeStartError(ex, port), ex);
            }

            _audio = audio;
            _app = app;
            Port = port;

            Log.Info($"Puente activado en el puerto {port}.");
            Log.Info($"Capturando: {audio.DeviceName} ({audio.SourceDescription}).");
            SetState(BridgeState.Running);
            AudioChanged?.Invoke();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (State != BridgeState.Running)
                return;

            SetState(BridgeState.Stopping);

            _hub.DisconnectAll();

            var app = _app;
            _app = null;
            if (app is not null)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await app.StopAsync(timeout.Token);
                }
                catch
                {
                    // Se fuerza el cierre de todos modos.
                }
                await app.DisposeAsync();
            }

            var audio = _audio;
            _audio = null;
            if (audio is not null)
            {
                audio.FormatChanged -= OnAudioFormatChanged;
                audio.BlockReady -= _hub.Broadcast;
                await Task.Run(audio.Dispose);
            }

            Log.Info("Puente desactivado.");
            SetState(BridgeState.Stopped);
            AudioChanged?.Invoke();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

    private void OnAudioFormatChanged()
    {
        // Si cambia el formato, los celulares deben recibir el encabezado nuevo:
        // se cierran las conexiones y la pagina se reconecta sola en 1 segundo.
        if (_hub.Count > 0)
            _hub.DisconnectAll();
        AudioChanged?.Invoke();
    }

    private WebApplication BuildWebApp(int port, Func<int> sampleRate)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Listen(IPAddress.Any, port);
        });

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });

        app.MapGet("/", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(_indexHtml, "text/html; charset=utf-8");
        });

        app.Map("/ws", (HttpContext context) => AudioWebSocketHandler.HandleAsync(context, _hub, sampleRate()));

        return app;
    }

    private void SetState(BridgeState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static string DescribeStartError(Exception ex, int port)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is IOException || e is System.Net.Sockets.SocketException)
                return $"No se pudo abrir el puerto {port}. Puede que otro programa lo este usando; prueba con otro puerto.";
        }
        return $"No se pudo activar el puente: {ex.Message}";
    }

    private static byte[] LoadIndexHtml()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MobileSpeaker.Web.index.html")
            ?? throw new InvalidOperationException("No se encontro la pagina embebida (Web/index.html).");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

public sealed class BridgeStartException : Exception
{
    public BridgeStartException(string message, Exception inner) : base(message, inner) { }
}
