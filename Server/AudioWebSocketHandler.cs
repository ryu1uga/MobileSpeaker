using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace MobileSpeaker.Server;

/// <summary>
/// Atiende /ws: envia primero un JSON con el formato y luego bloques binarios de PCM 16 bits estereo.
/// </summary>
public static class AudioWebSocketHandler
{
    public static async Task HandleAsync(HttpContext context, ClientHub hub, int sampleRate)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Se esperaba una conexion WebSocket.");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var token = cts.Token;

        string remote = FormatRemote(context.Connection.RemoteIpAddress);
        var (id, reader) = hub.Add();
        Log.Info($"Cliente conectado: {remote} (clientes: {hub.Count})");

        Task receiveTask = Task.CompletedTask;
        try
        {
            string header = $"{{\"sampleRate\":{sampleRate},\"channels\":2}}";
            await socket.SendAsync(Encoding.UTF8.GetBytes(header), WebSocketMessageType.Text, true, token);

            receiveTask = ReceiveUntilClosedAsync(socket, cts);

            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var block))
                {
                    await socket.SendAsync(new ArraySegment<byte>(block), WebSocketMessageType.Binary, true, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            hub.Remove(id);
            cts.Cancel();
            await receiveTask;
            await TryCloseAsync(socket);
            Log.Info($"Cliente desconectado: {remote} (clientes: {hub.Count})");
        }
    }

    /// <summary>
    /// El cliente no envia datos; solo se lee para detectar el cierre de la conexion.
    /// </summary>
    private static async Task ReceiveUntilClosedAsync(WebSocket socket, CancellationTokenSource cts)
    {
        var buffer = new byte[1024];
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch
        {
            // Cualquier error de lectura equivale a desconexion.
        }
        finally
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private static async Task TryCloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "fin", timeout.Token);
            }
        }
        catch
        {
            // La conexion ya estaba cerrada.
        }
    }

    private static string FormatRemote(IPAddress? address)
    {
        if (address is null)
            return "desconocido";
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
    }
}
