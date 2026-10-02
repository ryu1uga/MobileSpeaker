using System.Drawing;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using MobileSpeaker.Core;

namespace MobileSpeaker.UI;

/// <summary>
/// Ventana principal: activar o desactivar el puente, ver la direccion para el celular,
/// opciones y registro. Al minimizar queda en la bandeja del sistema.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(246, 247, 249);
    private static readonly Color CardColor = Color.White;
    private static readonly Color TextColor = Color.FromArgb(28, 32, 40);
    private static readonly Color MutedColor = Color.FromArgb(105, 112, 125);
    private static readonly Color AccentColor = Color.FromArgb(47, 111, 235);
    private static readonly Color OkColor = Color.FromArgb(46, 170, 100);
    private static readonly Color WarnColor = Color.FromArgb(222, 150, 40);
    private static readonly Color ErrorColor = Color.FromArgb(214, 69, 65);
    private static readonly Color IdleColor = Color.FromArgb(160, 166, 176);
    private static readonly Color NeutralButtonColor = Color.FromArgb(228, 231, 236);

    private const int MaxLogLines = 400;

    private readonly BridgeService _bridge = new();
    private readonly AppSettings _settings;
    private readonly EventWaitHandle _showEvent;
    private readonly bool _startHidden;
    private RegisteredWaitHandle? _showWaitRegistration;

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayToggleItem;
    private readonly ToolStripMenuItem _trayCopyItem;

    private readonly StatusDot _statusDot;
    private readonly Label _statusTitle;
    private readonly Label _statusDetail;
    private readonly Button _toggleButton;
    private readonly TextBox _urlBox;
    private readonly Button _copyButton;
    private readonly Label _otherUrlsLabel;
    private readonly ListBox _otherUrls;
    private readonly Label _deviceLabel;
    private readonly Label _formatLabel;
    private readonly NumericUpDown _portInput;
    private readonly CheckBox _autoStartCheck;
    private readonly CheckBox _windowsStartupCheck;
    private readonly TextBox _logBox;

    private List<LocalAddress> _addresses = new();
    private bool _hiddenOnce;
    private bool _autoStartDone;
    private bool _trayHintShown;
    private bool _exitRequested;
    private bool _readyToClose;
    private bool _loadingOptions;
    private string? _lastError;

    public MainForm(bool startHidden, EventWaitHandle showEvent)
    {
        _startHidden = startHidden;
        _showEvent = showEvent;
        _settings = AppSettings.Load();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
        Text = "MobileSpeaker";
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 640);
        ClientSize = new Size(500, 720);

        Icon? appIcon = LoadIcon(null);
        if (appIcon is not null)
            Icon = appIcon;

        // ---------- Encabezado ----------
        var title = new Label
        {
            Text = "MobileSpeaker",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 2)
        };
        var subtitle = new Label
        {
            Text = "Usa tu celular como parlante de la PC por Wi-Fi.",
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0, 0, 0, 12)
        };

        // ---------- Estado ----------
        _statusDot = new StatusDot { Size = new Size(14, 14), Margin = new Padding(0, 5, 10, 0), DotColor = IdleColor };
        _statusTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 2)
        };
        _statusDetail = new Label
        {
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0),
            MaximumSize = new Size(400, 0)
        };
        var statusText = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Dock = DockStyle.Fill
        };
        statusText.Controls.Add(_statusTitle, 0, 0);
        statusText.Controls.Add(_statusDetail, 0, 1);

        var statusRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        statusRow.Controls.Add(_statusDot, 0, 0);
        statusRow.Controls.Add(statusText, 1, 0);
        var statusCard = Card(statusRow);

        // ---------- Boton principal ----------
        _toggleButton = new Button
        {
            Dock = DockStyle.Fill,
            Height = 46,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 11.25F, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 0, 12),
            UseVisualStyleBackColor = false
        };
        _toggleButton.FlatAppearance.BorderSize = 0;
        _toggleButton.Click += async (_, _) => await ToggleBridgeAsync(interactive: true);

        // ---------- Direccion ----------
        var urlCaption = new Label
        {
            Text = "Abre esta direccion en el navegador del celular:",
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0, 0, 0, 4)
        };
        _urlBox = new TextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = CardColor,
            Font = new Font("Segoe UI Semibold", 14.25F, FontStyle.Bold, GraphicsUnit.Point),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 0),
            TabStop = false
        };
        _copyButton = new Button
        {
            Text = "Copiar",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = NeutralButtonColor,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0),
            Padding = new Padding(8, 2, 8, 2),
            Cursor = Cursors.Hand
        };
        _copyButton.FlatAppearance.BorderSize = 0;
        _copyButton.Click += (_, _) => CopyMainUrl();

        var urlRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6)
        };
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        urlRow.Controls.Add(_urlBox, 0, 0);
        urlRow.Controls.Add(_copyButton, 1, 0);

        var urlHint = new Label
        {
            Text = "El celular debe estar en la misma red Wi-Fi que esta PC. Toca \"Reproducir\" en la pagina.",
            AutoSize = true,
            ForeColor = MutedColor,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(0, 0, 0, 6)
        };
        _otherUrlsLabel = new Label
        {
            Text = "Otras direcciones (doble clic para copiar):",
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0, 4, 0, 4)
        };
        _otherUrls = new ListBox
        {
            Dock = DockStyle.Fill,
            Height = 64,
            IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0)
        };
        _otherUrls.DoubleClick += (_, _) => CopyOtherUrl();

        var urlLayout = Stack(urlCaption, urlRow, urlHint, _otherUrlsLabel, _otherUrls);
        var urlCard = Card(urlLayout);

        // ---------- Audio ----------
        _deviceLabel = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 2), MaximumSize = new Size(440, 0) };
        _formatLabel = new Label { AutoSize = true, ForeColor = MutedColor, Margin = new Padding(0), MaximumSize = new Size(440, 0) };
        var audioCard = Card(Stack(_deviceLabel, _formatLabel));

        // ---------- Opciones ----------
        var portLabel = new Label { Text = "Puerto:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
        _portInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Width = 90,
            Margin = new Padding(0, 2, 0, 4),
            TextAlign = HorizontalAlignment.Right
        };
        _portInput.ValueChanged += (_, _) =>
        {
            if (_loadingOptions)
                return;
            _settings.Port = (int)_portInput.Value;
            _settings.Save();
            RefreshAddresses();
        };
        var portRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0),
            FlowDirection = FlowDirection.LeftToRight
        };
        portRow.Controls.Add(portLabel);
        portRow.Controls.Add(_portInput);

        _autoStartCheck = new CheckBox
        {
            Text = "Activar el puente al abrir el programa",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 2)
        };
        _autoStartCheck.CheckedChanged += (_, _) =>
        {
            if (_loadingOptions)
                return;
            _settings.AutoStartBridge = _autoStartCheck.Checked;
            _settings.Save();
        };

        _windowsStartupCheck = new CheckBox
        {
            Text = "Iniciar con Windows (minimizado en la bandeja)",
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0)
        };
        _windowsStartupCheck.CheckedChanged += (_, _) =>
        {
            if (_loadingOptions)
                return;
            if (!WindowsStartup.SetEnabled(_windowsStartupCheck.Checked))
            {
                _loadingOptions = true;
                _windowsStartupCheck.Checked = WindowsStartup.IsEnabled();
                _loadingOptions = false;
            }
        };
        var optionsCard = Card(Stack(portRow, _autoStartCheck, _windowsStartupCheck));

        // ---------- Registro ----------
        var logCaption = new Label
        {
            Text = "Registro",
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0, 0, 0, 4)
        };
        _logBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = CardColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0),
            MinimumSize = new Size(0, 90),
            WordWrap = true
        };

        // ---------- Composicion ----------
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16, 14, 16, 16),
            AutoScroll = true
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        Control[] rows = { title, subtitle, statusCard, _toggleButton, urlCard, audioCard, optionsCard, logCaption, _logBox };
        root.RowCount = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            root.RowStyles.Add(i == rows.Length - 1
                ? new RowStyle(SizeType.Percent, 100F)
                : new RowStyle(SizeType.AutoSize));
            root.Controls.Add(rows[i], 0, i);
        }
        Controls.Add(root);

        // ---------- Bandeja del sistema ----------
        _trayToggleItem = new ToolStripMenuItem("Activar puente");
        _trayToggleItem.Click += async (_, _) => await ToggleBridgeAsync(interactive: false);
        _trayCopyItem = new ToolStripMenuItem("Copiar direccion");
        _trayCopyItem.Click += (_, _) => CopyMainUrl();
        var openItem = new ToolStripMenuItem("Abrir MobileSpeaker");
        openItem.Font = new Font(openItem.Font, FontStyle.Bold);
        openItem.Click += (_, _) => ShowFromTray();
        var exitItem = new ToolStripMenuItem("Salir");
        exitItem.Click += (_, _) => ExitApplication();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.AddRange(new ToolStripItem[] { openItem, _trayToggleItem, _trayCopyItem, new ToolStripSeparator(), exitItem });

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(SystemInformation.SmallIconSize) ?? SystemIcons.Application,
            Text = "MobileSpeaker",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        ResumeLayout(false);
        PerformLayout();

        // ---------- Eventos ----------
        Log.Message += OnLogMessage;
        _bridge.StateChanged += _ => RunOnUi(UpdateStatus);
        _bridge.ClientsChanged += _ => RunOnUi(UpdateStatus);
        _bridge.AudioChanged += () => RunOnUi(UpdateAudioInfo);
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        _loadingOptions = true;
        _portInput.Value = _settings.Port;
        _autoStartCheck.Checked = _settings.AutoStartBridge;
        _windowsStartupCheck.Checked = WindowsStartup.IsEnabled();
        _loadingOptions = false;

        RefreshAddresses();
        UpdateStatus();
        UpdateAudioInfo();
    }

    // ---------- Ciclo de vida ----------

    protected override void SetVisibleCore(bool value)
    {
        // Al iniciar con Windows se arranca oculto en la bandeja.
        if (_startHidden && !_hiddenOnce)
        {
            _hiddenOnce = true;
            if (!IsHandleCreated)
                CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        _showWaitRegistration ??= ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => RunOnUi(ShowFromTray), null, Timeout.Infinite, false);

        if (_settings.AutoStartBridge && !_autoStartDone)
        {
            _autoStartDone = true;
            BeginInvoke(new Action(async () => await ToggleBridgeAsync(interactive: false, startOnly: true)));
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(3000, "MobileSpeaker",
                    "Sigue funcionando en la bandeja del sistema. Doble clic en el icono para abrirlo.", ToolTipIcon.Info);
            }
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_readyToClose || e.CloseReason == CloseReason.WindowsShutDown)
        {
            _tray.Visible = false;
            base.OnFormClosing(e);
            return;
        }

        // Cerrar la ventana sale del programa: primero se desactiva el puente.
        e.Cancel = true;
        if (_exitRequested)
            return;
        _exitRequested = true;

        Hide();
        _tray.Visible = false;
        try
        {
            await _bridge.DisposeAsync();
        }
        catch
        {
            // Se sale de todos modos.
        }

        _readyToClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.Message -= OnLogMessage;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _showWaitRegistration?.Unregister(null);
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- Acciones ----------

    private async Task ToggleBridgeAsync(bool interactive, bool startOnly = false)
    {
        switch (_bridge.State)
        {
            case BridgeState.Running when !startOnly:
                await _bridge.StopAsync();
                break;

            case BridgeState.Stopped:
                _lastError = null;
                try
                {
                    await _bridge.StartAsync((int)_portInput.Value);
                }
                catch (BridgeStartException ex)
                {
                    _lastError = ex.Message;
                    Log.Error(ex.Message);
                    UpdateStatus();
                    if (interactive && Visible)
                        MessageBox.Show(this, ex.Message, "MobileSpeaker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    else
                        _tray.ShowBalloonTip(5000, "MobileSpeaker", ex.Message, ToolTipIcon.Warning);
                }
                break;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void ExitApplication() => Close();

    private void CopyMainUrl()
    {
        if (_addresses.Count == 0)
            return;
        CopyToClipboard(BuildUrl(_addresses[0]));
    }

    private void CopyOtherUrl()
    {
        int index = _otherUrls.SelectedIndex + 1;
        if (index >= 1 && index < _addresses.Count)
            CopyToClipboard(BuildUrl(_addresses[index]));
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            Log.Info($"Copiado: {text}");
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo copiar al portapapeles: {ex.Message}");
        }
    }

    // ---------- Actualizacion de la interfaz ----------

    private void UpdateStatus()
    {
        var state = _bridge.State;
        bool running = state == BridgeState.Running;
        int clients = _bridge.ClientCount;

        switch (state)
        {
            case BridgeState.Starting:
                SetStatus(WarnColor, "Activando...", "Preparando la captura de audio y el servidor.");
                break;
            case BridgeState.Stopping:
                SetStatus(WarnColor, "Desactivando...", string.Empty);
                break;
            case BridgeState.Running when string.IsNullOrEmpty(_bridge.DeviceName):
                SetStatus(WarnColor, "Puente activo, sin dispositivo de audio",
                    "Windows no tiene una salida de audio activa. Se reanudara al conectar una.");
                break;
            case BridgeState.Running when clients == 0:
                SetStatus(OkColor, "Puente activo", "Esperando celulares. Abre la direccion de abajo en el celular.");
                break;
            case BridgeState.Running:
                SetStatus(OkColor, "Puente activo",
                    clients == 1 ? "1 celular conectado." : $"{clients} celulares conectados.");
                break;
            default:
                if (_lastError is not null)
                    SetStatus(ErrorColor, "No se pudo activar el puente", _lastError);
                else
                    SetStatus(IdleColor, "Puente desactivado", "Activalo para escuchar el audio de la PC en el celular.");
                break;
        }

        bool busy = state is BridgeState.Starting or BridgeState.Stopping;
        _toggleButton.Enabled = !busy;
        _toggleButton.Text = running ? "Desactivar puente" : busy ? "Espera..." : "Activar puente";
        _toggleButton.BackColor = running ? NeutralButtonColor : AccentColor;
        _toggleButton.ForeColor = running ? TextColor : Color.White;

        _portInput.Enabled = state == BridgeState.Stopped;
        _urlBox.ForeColor = running ? AccentColor : MutedColor;

        _trayToggleItem.Text = running ? "Desactivar puente" : "Activar puente";
        _trayToggleItem.Enabled = !busy;
        _trayCopyItem.Enabled = _addresses.Count > 0;

        string trayText = running
            ? (clients == 0 ? "MobileSpeaker - activo" : $"MobileSpeaker - activo ({clients} conectado{(clients == 1 ? "" : "s")})")
            : "MobileSpeaker - desactivado";
        _tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }

    private void SetStatus(Color color, string title, string detail)
    {
        _statusDot.DotColor = color;
        _statusTitle.Text = title;
        _statusDetail.Text = detail;
        _statusDetail.Visible = detail.Length > 0;
    }

    private void UpdateAudioInfo()
    {
        if (_bridge.State == BridgeState.Running && !string.IsNullOrEmpty(_bridge.DeviceName))
        {
            _deviceLabel.Text = $"Dispositivo: {_bridge.DeviceName}";
            _formatLabel.Text = $"Formato de Windows: {_bridge.SourceDescription}. Se envia PCM 16 bits estereo a {_bridge.SampleRate} Hz.";
        }
        else
        {
            _deviceLabel.Text = "Dispositivo: se captura la salida de audio predeterminada de Windows.";
            _formatLabel.Text = "Si cambias de parlante o audifonos, el puente se adapta solo.";
        }
        UpdateStatus();
    }

    private void RefreshAddresses()
    {
        _addresses = NetworkInfo.GetLocalIPv4Addresses();

        if (_addresses.Count == 0)
        {
            _urlBox.Text = "Sin red: conecta la PC al Wi-Fi";
            _otherUrls.Items.Clear();
            _otherUrlsLabel.Visible = false;
            _otherUrls.Visible = false;
            _copyButton.Enabled = false;
            UpdateStatus();
            return;
        }

        _urlBox.Text = BuildUrl(_addresses[0]);
        _copyButton.Enabled = true;

        _otherUrls.BeginUpdate();
        _otherUrls.Items.Clear();
        foreach (var address in _addresses.Skip(1))
        {
            string note = address.IsVirtual ? ", adaptador virtual, normalmente no sirve" : string.Empty;
            _otherUrls.Items.Add($"{BuildUrl(address)}   ({address.InterfaceName}{note})");
        }
        _otherUrls.EndUpdate();

        bool hasOthers = _addresses.Count > 1;
        _otherUrlsLabel.Visible = hasOthers;
        _otherUrls.Visible = hasOthers;
        UpdateStatus();
    }

    private string BuildUrl(LocalAddress address) => $"http://{address.Address}:{(int)_portInput.Value}";

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => RunOnUi(RefreshAddresses);

    private void OnLogMessage(LogKind level, string line) => RunOnUi(() => AppendLog(level, line));

    private void AppendLog(LogKind level, string line)
    {
        string prefix = level switch
        {
            LogKind.Warn => "Aviso: ",
            LogKind.Error => "Error: ",
            _ => string.Empty
        };

        // Insertar el prefijo despues de la hora "[HH:mm:ss] ".
        int split = line.IndexOf("] ", StringComparison.Ordinal);
        string text = split >= 0 ? line[..(split + 2)] + prefix + line[(split + 2)..] : prefix + line;

        var lines = _logBox.Lines;
        if (lines.Length >= MaxLogLines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines.Skip(lines.Length - MaxLogLines / 2))
                sb.AppendLine(l);
            _logBox.Text = sb.ToString();
        }

        _logBox.AppendText(text + Environment.NewLine);
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    // ---------- Utilidades de diseno ----------

    private static TableLayoutPanel Card(Control content)
    {
        content.Dock = DockStyle.Fill;
        var card = new TableLayoutPanel
        {
            BackColor = CardColor,
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(0, 0, 0, 12),
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 1
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(content, 0, 0);
        return card;
    }

    private static TableLayoutPanel Stack(params Control[] controls)
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = controls.Length,
            Margin = new Padding(0),
            Dock = DockStyle.Fill
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int i = 0; i < controls.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(controls[i], 0, i);
        }
        return layout;
    }

    private static Icon? LoadIcon(Size? size)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MobileSpeaker.app.ico");
            if (stream is null)
                return null;
            return size.HasValue ? new Icon(stream, size.Value) : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}
