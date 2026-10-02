using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace MobileSpeaker.Audio;

/// <summary>
/// Captura el dispositivo de salida por defecto y entrega bloques PCM 16 bits estereo.
/// Si Windows cambia el dispositivo de salida por defecto, la captura se reinicia sola.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int CaptureBufferMs = 20;

    private readonly object _sync = new();
    private MMDeviceEnumerator? _enumerator;
    private DeviceNotificationClient? _notifications;
    private MMDevice? _device;
    private LowLatencyLoopbackCapture? _capture;
    private PcmConverter? _converter;
    private System.Threading.Timer? _restartTimer;
    private bool _running;

    /// <summary>Bloque de audio ya convertido. Se invoca en el hilo de captura.</summary>
    public event Action<byte[]>? BlockReady;

    /// <summary>Se dispara cuando la captura arranca o se reinicia con otro dispositivo o formato.</summary>
    public event Action? FormatChanged;

    /// <summary>Si es false, no se convierte nada (no hay clientes conectados).</summary>
    public Func<bool> HasListeners { get; set; } = () => true;

    public int SampleRate { get; private set; }
    public string DeviceName { get; private set; } = string.Empty;
    public string SourceDescription { get; private set; } = string.Empty;

    public void Start()
    {
        lock (_sync)
        {
            if (_running)
                return;

            _enumerator = new MMDeviceEnumerator();
            _notifications = new DeviceNotificationClient(ScheduleRestart);
            _enumerator.RegisterEndpointNotificationCallback(_notifications);

            try
            {
                StartCaptureLocked();
            }
            catch
            {
                CleanupLocked();
                throw;
            }

            _running = true;
        }

        FormatChanged?.Invoke();
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_running)
                return;

            _running = false;
            CleanupLocked();
        }
    }

    public void Dispose() => Stop();

    private void StartCaptureLocked()
    {
        var enumerator = _enumerator ?? throw new InvalidOperationException("Enumerador no inicializado.");
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            throw new InvalidOperationException("No hay un dispositivo de salida de audio activo.");

        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        LowLatencyLoopbackCapture? capture = null;
        try
        {
            capture = new LowLatencyLoopbackCapture(device, CaptureBufferMs);
            var converter = new PcmConverter(capture.WaveFormat);

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();

            _device = device;
            _capture = capture;
            _converter = converter;
            SampleRate = converter.SampleRate;
            DeviceName = device.FriendlyName;
            SourceDescription = converter.SourceDescription;
        }
        catch
        {
            capture?.Dispose();
            device.Dispose();
            throw;
        }
    }

    private void StopCaptureLocked()
    {
        var capture = _capture;
        _capture = null;
        _converter = null;

        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); } catch { }
            try { capture.Dispose(); } catch { }
        }

        try { _device?.Dispose(); } catch { }
        _device = null;
    }

    private void CleanupLocked()
    {
        _restartTimer?.Dispose();
        _restartTimer = null;

        StopCaptureLocked();

        if (_enumerator is not null && _notifications is not null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); } catch { }
        }
        _notifications = null;

        try { _enumerator?.Dispose(); } catch { }
        _enumerator = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || !HasListeners())
            return;

        var converter = _converter;
        if (converter is null)
            return;

        var block = converter.Convert(e.Buffer, e.BytesRecorded);
        if (block.Length > 0)
            BlockReady?.Invoke(block);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Warn($"La captura se detuvo: {e.Exception.Message}. Reintentando...");
            // No bloquear el hilo de captura: Stop/Dispose espera a que termine.
            ThreadPool.QueueUserWorkItem(_ => ScheduleRestart());
        }
    }

    /// <summary>
    /// Las notificaciones de Windows llegan en un hilo COM donde no se debe volver a llamar
    /// a la API de audio, y suelen llegar varias seguidas. Se agrupan y se procesan despues.
    /// </summary>
    private void ScheduleRestart()
    {
        lock (_sync)
        {
            if (!_running)
                return;

            _restartTimer?.Dispose();
            _restartTimer = new System.Threading.Timer(_ => RestartCapture(), null, 400, Timeout.Infinite);
        }
    }

    private void RestartCapture()
    {
        bool restarted = false;
        lock (_sync)
        {
            if (!_running)
                return;

            string previous = DeviceName;
            StopCaptureLocked();
            try
            {
                StartCaptureLocked();
                restarted = true;
                if (previous != DeviceName)
                    Log.Info($"Dispositivo de salida cambiado: {DeviceName}");
                else
                    Log.Info($"Captura reiniciada: {DeviceName}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Sin dispositivo de salida disponible ({ex.Message}). Se reintentara al conectar uno.");
                DeviceName = string.Empty;
                SourceDescription = string.Empty;
            }
        }

        if (restarted)
            FormatChanged?.Invoke();
    }

    private sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action _onChange;

        public DeviceNotificationClient(Action onChange) => _onChange = onChange;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Nunca llamar a la API de audio ni bloquear dentro de la notificacion.
            if (flow == DataFlow.Render && role == Role.Multimedia)
                ThreadPool.QueueUserWorkItem(_ => _onChange());
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }

        public void OnDeviceAdded(string pwstrDeviceId) { }

        public void OnDeviceRemoved(string deviceId) { }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
