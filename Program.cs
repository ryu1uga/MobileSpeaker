using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using MobileSpeaker;
using MobileSpeaker.Audio;
using MobileSpeaker.Server;
using NAudio.CoreAudioApi;

const int DefaultPort = 8765;
const int CaptureBufferMs = 20;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "MobileSpeaker";

// ---------- Argumentos ----------
int port = DefaultPort;
if (args.Length > 0)
{
    if (!int.TryParse(args[0], out port) || port < 1 || port > 65535)
    {
        Log.Error($"Puerto invalido: \"{args[0]}\". Uso: MobileSpeaker.exe [puerto]  (por defecto {DefaultPort})");
        return 1;
    }
}

// ---------- Pagina embebida ----------
byte[] indexHtml;
using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MobileSpeaker.Web.index.html"))
{
    if (stream is null)
    {
        Log.Error("No se encontro la pagina embebida (Web/index.html).");
        return 1;
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    indexHtml = ms.ToArray();
}

// ---------- Captura de audio ----------
var deviceEnumerator = new MMDeviceEnumerator();
MMDevice device;
LowLatencyLoopbackCapture capture;
PcmConverter converter;
try
{
    device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    capture = new LowLatencyLoopbackCapture(device, CaptureBufferMs);
    converter = new PcmConverter(capture.WaveFormat);
}
catch (Exception ex)
{
    Log.Error($"No se pudo preparar la captura del dispositivo de salida: {ex.Message}");
    return 1;
}

var hub = new ClientHub();

capture.DataAvailable += (_, e) =>
{
    if (e.BytesRecorded <= 0 || hub.Count == 0)
        return;

    var block = converter.Convert(e.Buffer, e.BytesRecorded);
    if (block.Length > 0)
        hub.Broadcast(block);
};

capture.RecordingStopped += (_, e) =>
{
    if (e.Exception is not null)
    {
        Log.Error($"La captura se detuvo: {e.Exception.Message}");
        Log.Error("Si cambiaste el dispositivo de salida, cierra y vuelve a abrir el programa.");
    }
};

// ---------- Servidor web ----------
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
builder.Logging.ClearProviders();
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
    return Results.Bytes(indexHtml, "text/html; charset=utf-8");
});

app.Map("/ws", (HttpContext context) => AudioWebSocketHandler.HandleAsync(context, hub, converter.SampleRate));

try
{
    await app.StartAsync();
}
catch (Exception ex)
{
    Log.Error($"No se pudo abrir el puerto {port}: {ex.Message}");
    Log.Error("Puede que otro programa lo este usando. Prueba con otro puerto: MobileSpeaker.exe 8766");
    capture.Dispose();
    return 1;
}

try
{
    capture.StartRecording();
}
catch (Exception ex)
{
    Log.Error($"No se pudo iniciar la captura: {ex.Message}");
    await app.StopAsync();
    capture.Dispose();
    return 1;
}

// ---------- Informacion en consola ----------
Console.WriteLine();
Console.WriteLine("  MobileSpeaker - parlante de la PC por Wi-Fi");
Console.WriteLine("  -------------------------------------------");
Console.WriteLine($"  Dispositivo capturado : {device.FriendlyName}");
Console.WriteLine($"  Formato de Windows    : {converter.SourceDescription}");
Console.WriteLine($"  Formato enviado       : {converter.SampleRate} Hz, PCM 16 bits, 2 canales");
Console.WriteLine($"  Buffer de captura     : {CaptureBufferMs} ms");
Console.WriteLine();

var urls = GetLocalIPv4Addresses().Select(ip => $"http://{ip}:{port}").ToList();
if (urls.Count == 0)
{
    Log.Warn("No se encontro ninguna IPv4 local. Verifica que la PC este conectada a la red.");
}
else
{
    Console.WriteLine("  Abre una de estas direcciones en el navegador del celular (en la misma red Wi-Fi):");
    foreach (var url in urls)
        Console.WriteLine($"    {url}");
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("  Importante: si Windows pregunta por el firewall, permite el acceso en \"Redes privadas\".");
Console.WriteLine("  Si el celular no carga la pagina, revisa que la red Wi-Fi este marcada como privada");
Console.WriteLine("  y que MobileSpeaker este permitido en el Firewall de Windows para redes privadas.");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("  Presiona Ctrl+C para salir.");
Console.WriteLine();

await app.WaitForShutdownAsync();

try
{
    capture.StopRecording();
}
catch
{
    // Ya estaba detenida.
}
capture.Dispose();
device.Dispose();
deviceEnumerator.Dispose();
return 0;

static IEnumerable<IPAddress> GetLocalIPv4Addresses()
{
    var result = new List<IPAddress>();
    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (nic.OperationalStatus != OperationalStatus.Up ||
            nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            continue;

        foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
        {
            var ip = unicast.Address;
            if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                continue;

            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254)
                continue;

            if (!result.Contains(ip))
                result.Add(ip);
        }
    }
    return result;
}
