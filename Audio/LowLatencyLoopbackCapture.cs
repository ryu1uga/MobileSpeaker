using NAudio.CoreAudioApi;

namespace MobileSpeaker.Audio;

/// <summary>
/// Captura WASAPI en modo loopback con un buffer corto.
/// WasapiLoopbackCapture de NAudio usa 100 ms; aqui se usa el valor indicado (20 ms por defecto)
/// para reducir la latencia de captura.
/// </summary>
public sealed class LowLatencyLoopbackCapture : WasapiCapture
{
    public LowLatencyLoopbackCapture(MMDevice renderDevice, int bufferMilliseconds = 20)
        : base(renderDevice, false, bufferMilliseconds)
    {
    }

    protected override AudioClientStreamFlags GetAudioClientStreamFlags()
    {
        return AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
    }
}
