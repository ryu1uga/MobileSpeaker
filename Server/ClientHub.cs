using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MobileSpeaker.Server;

/// <summary>
/// Reparte cada bloque de audio a todos los clientes conectados.
/// Cada cliente tiene una cola acotada que descarta lo mas antiguo, asi un cliente lento
/// pierde audio en lugar de acumular retraso.
/// </summary>
public sealed class ClientHub
{
    public const int QueueCapacity = 8;

    private readonly ConcurrentDictionary<int, Channel<byte[]>> _clients = new();
    private int _nextId;

    public int Count => _clients.Count;

    public (int Id, ChannelReader<byte[]> Reader) Add()
    {
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        int id = Interlocked.Increment(ref _nextId);
        _clients[id] = channel;
        return (id, channel.Reader);
    }

    public void Remove(int id)
    {
        if (_clients.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }

    public void Broadcast(byte[] block)
    {
        foreach (var channel in _clients.Values)
            channel.Writer.TryWrite(block);
    }
}
