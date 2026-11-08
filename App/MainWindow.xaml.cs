using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using VmixSubtitleController.Output;

namespace VmixSubtitleController;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _timer;
    private readonly ChatGptAccount _chatGptAccount;
    private readonly ChatGptTranslator _translator;
    private AppPreferences _preferences;
    private CancellationTokenSource? _signInCancellation;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Slot> _slots = Enumerable.Range(1, 4).Select(i => new Slot(i)).ToList();
    private List<VmixInput> _inputs = new();
    private Dictionary<string, string> _vmixTitleFields = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _vmixTitleVisibilityFields = new(StringComparer.OrdinalIgnoreCase);
    private List<VideoPlaylist> _playlists = new();
    private string? _selectedPlaylistId;
    private string? _playingPlaylistId;
    private string? _broadcastCardId;
    private string? _broadcastCardInputKey;
    private readonly List<string> _playlistQueue = new();
    private int _playlistIndex = -1;
    private bool _playlistActive;
    private DateTime _playlistTransitionUntil;
    private readonly ISubtitleOutput _vmixSubtitleOutput;
    private ObsWebSocketClient? _obsClient;
    private readonly HashSet<string> _obsInputNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string?> _vmixSlotInputKeys = new();
    private readonly Dictionary<int, string?> _obsSlotInputKeys = new();
    private string? _vmixTitleKey;
    private string? _obsTitleKey;
    private List<string> _obsFoundHosts = new();
    private string _obsStatus = "OBS bağlı değil";
    private string _outputMode = "vmix";
    private bool _obsScanning;
    private string? _lastProgramInput;
    private string? _vmixProgramInputKey;
    private string? _vmixPreviewInputKey;
    private double? _videoPositionMilliseconds;
    private double? _videoDurationMilliseconds;
    private bool _videoIsPlaying;
    private readonly Dictionary<string,double> _vmixDurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,double> _obsDurations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,string> _vmixInputNames = new(StringComparer.OrdinalIgnoreCase);
    private string _videoSource = "";
    private string? _selectedTitleKey;
    private string? _apiBase;
    private string _lastSentText = "\u0000";
    private bool _tickRunning;
    private ISubtitleOutput _subtitleOutput;
    private readonly HashSet<string> _titlesToClear = new(StringComparer.OrdinalIgnoreCase);
    private int _revision;
    private DateTime _retryAfter;
    private bool _closed;
    private bool _refreshing;
    private bool _updatingInputLists;
    private int _sessionTranslations;
    private int _sessionTranslatedCues;
    private DateTime? _lastTranslationActivity;
    private string _usageStatus = "Bu oturumda henüz çeviri yapılmadı";
    private string[] _omtSources = Array.Empty<string>();
    private OmtVideoReceiver? _omtReceiver;
    private string _omtSource = "";

    public MainWindow()
    {
        _chatGptAccount = new ChatGptAccount();
        _translator = new ChatGptTranslator(_chatGptAccount);
        _vmixSubtitleOutput = new VmixSubtitleOutput(_http, () => _apiBase, () => _selectedTitleKey,
            key => _vmixTitleFields.TryGetValue(key, out var field) ? field : "Message.Text",
            key => _vmixTitleVisibilityFields.TryGetValue(key, out var field) ? field : null);
        _subtitleOutput = _vmixSubtitleOutput;
        try { _preferences = AppPreferences.Load(); _activeProfilePath = ReadActiveProfilePath(); }
        catch { _preferences = new(); }
        if (_activeProfilePath != null)
        {
            RegisterProfilePath(_activeProfilePath);
            try { var profile = SubtitleProfile.Load(_activeProfilePath); _glossaryEntries = profile.Glossary.ToList(); _playlists = profile.Playlists.ToList(); _selectedPlaylistId = _playlists.FirstOrDefault()?.Id; }
            catch { _glossaryEntries = new(); }
        }
        InitializeComponent();
        Title = "im too lazy";
        AppNameText.Text = "▣  im too lazy";
        BuildSlots();
        InitializeDashboard();
        UpdateChatGptStatus();
        Loaded += async (_, _) =>
        {
            _webTheme = _preferences.ColorTheme;
            await InitializeWebAsync();
            _ = CheckForUpdateAsync();
            if (_chatGptAccount.CanTranslate) await RefreshChatGptModelsAsync();
            if (!_closed && _chatGptAccount.StartupWarning != null) ChatGptStatusText.Text = _chatGptAccount.StartupWarning;
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        Closed += async (_, _) =>
        {
            _closed = true; _timer.Stop();
            _lifetime.Cancel(); _signInCancellation?.Cancel();
            foreach (var slot in _slots) slot.TranslationCancellation?.Cancel();
            if (_obsClient != null) await _obsClient.DisposeAsync();
            StopOmtReceiver();
            WebSurface.Dispose();
            _translator.Dispose(); _chatGptAccount.Dispose(); _http.Dispose();
        };
    }

    private void BuildSlots()
    {
        SlotsPanel.Children.Clear();
        _slotRows.Clear();
        foreach (var slot in _slots) BuildSlotRow(slot);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshing) { GlobalStatus.Text = "vMix girişleri yenileniyor…"; return; }
        if (string.IsNullOrWhiteSpace(_apiBase)) { GlobalStatus.Text = "Girişleri yenilemek için önce vMix’e bağlanın."; ShowPage("connections"); return; }

        _refreshing = true;
        InvalidateSubtitle();
        try
        {
            GlobalStatus.Text = "Refreshing vMix inputs...";
            await RefreshInputsAsync();
        }
        catch (Exception ex)
        {
            GlobalStatus.Text = $"Refresh failed: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    private void TitleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingInputLists) return;
        var next = (TitleBox.SelectedItem as VmixInput)?.Key;
        if (_selectedTitleKey == next) return;
        if (_selectedTitleKey != null) _titlesToClear.Add(_selectedTitleKey);
        _selectedTitleKey = next;
        if (_outputMode == "obs") _obsTitleKey = next; else _vmixTitleKey = next;
        TitleStatusText.Text = next == null ? "Select subtitle title" : $"Subtitle: {(TitleBox.SelectedItem as VmixInput)?.Title}";
        InvalidateSubtitle();
    }

    private void SaveCurrentOutputMappings()
    {
        var inputMap = _outputMode == "obs" ? _obsSlotInputKeys : _vmixSlotInputKeys;
        foreach (var slot in _slots)if(!string.IsNullOrWhiteSpace(slot.InputKey))inputMap[slot.Number] = slot.InputKey;
        if(!string.IsNullOrWhiteSpace(_selectedTitleKey)){if (_outputMode == "obs") _obsTitleKey = _selectedTitleKey; else _vmixTitleKey = _selectedTitleKey;}
    }

    private async Task ClearCurrentOutputTextAsync()
    {
        foreach (var title in _titlesToClear.ToArray())
            await PublishSubtitleAsync("", title);
        _titlesToClear.Clear();
        if (!string.IsNullOrWhiteSpace(_selectedTitleKey))
            await PublishSubtitleAsync("", _selectedTitleKey);
    }

    private void InvalidateSubtitle()
    {
        _revision++;
        _lastSentText = "\u0000";
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try { await ConnectVmixAddressAsync(_apiBase ?? "http://localhost:8088/api/"); }
        catch (Exception ex) { GlobalStatus.Text = ex.Message; }
    }

    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch { continue; }

            foreach (var uni in props.UnicastAddresses)
            {
                if (uni.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(uni.Address))
                    yield return uni.Address.ToString();
            }
        }
    }

    private static IEnumerable<string> GetSubnetCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch { continue; }

            foreach (var uni in props.UnicastAddresses)
            {
                if (uni.Address.AddressFamily != AddressFamily.InterNetwork ||
                    uni.IPv4Mask == null)
                    continue;

                var ip = ToUInt32(uni.Address.GetAddressBytes());
                var mask = ToUInt32(uni.IPv4Mask.GetAddressBytes());
                var network = ip & mask;
                var broadcast = network | ~mask;
                var hostCount = broadcast > network ? broadcast - network - 1 : 0;

                // Avoid accidentally probing very large corporate networks.
                if (hostCount == 0 || hostCount > 512)
                    continue;

                for (uint value = network + 1; value < broadcast; value++)
                {
                    var address = FromUInt32(value).ToString();
                    var candidate = $"http://{address}:8088/api/";
                    if (seen.Add(candidate))
                        yield return candidate;
                }
            }
        }
    }

    private static uint ToUInt32(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    private static IPAddress FromUInt32(uint value) => new(new[]
    {
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    });

    private async Task RefreshInputsAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiBase)) return;

        try
        {
            var xml = await _http.GetStringAsync(_apiBase);
            var doc = XDocument.Parse(xml);
            UpdateVmixMediaInformation(doc);
            var titleFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var visibilityFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in doc.Descendants("input"))
            {
                var key = (string?)input.Attribute("key");
                if (string.IsNullOrWhiteSpace(key)) continue;
                var fields = input.Elements("text")
                    .Select(element => (string?)element.Attribute("name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToList();
                var field = fields.FirstOrDefault(name => name.Equals("Message.Text", StringComparison.OrdinalIgnoreCase))
                    ?? fields.FirstOrDefault(name => name.Equals("Message", StringComparison.OrdinalIgnoreCase))
                    ?? fields.FirstOrDefault();
                var visibilityField = fields.FirstOrDefault(name => name.Equals("SubtitleVisible", StringComparison.OrdinalIgnoreCase));
                if (visibilityField != null) visibilityFields[key] = visibilityField;
                var type = (string?)input.Attribute("type") ?? "";
                if (field != null) titleFields[key] = field;
                else if (type.Equals("GT", StringComparison.OrdinalIgnoreCase)) titleFields[key] = "Message.Text";
                else if (type.Equals("Title", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("Xaml", StringComparison.OrdinalIgnoreCase)) titleFields[key] = "Message";
            }
            _vmixTitleFields = titleFields;
            _vmixTitleVisibilityFields = visibilityFields;

            _inputs = doc.Descendants("input")
                .Select(x => new VmixInput(
                    (string?)x.Attribute("key") ?? "",
                    (string?)x.Attribute("number") ?? "",
                    (string?)x.Attribute("type") ?? "",
                    (string?)x.Attribute("title") ?? "",
                    (string?)x.Attribute("state") ?? ""))
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToList();

            var videoInputs = _inputs
                .Where(x => x.Type.Equals("Video", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var titleInputs = _inputs
                .Where(x => _vmixTitleFields.ContainsKey(x.Key))
                .ToList();

            _updatingInputLists = true;
            TitleBox.ItemsSource = titleInputs;
            if (_vmixTitleKey != null)
                TitleBox.SelectedItem = titleInputs.FirstOrDefault(x => x.Key == _vmixTitleKey);

            if (TitleBox.SelectedItem == null)
            {
                var preferred = titleInputs.FirstOrDefault(x =>
                    x.Title.Contains("Subtitle", StringComparison.OrdinalIgnoreCase));
                TitleBox.SelectedItem = preferred ?? titleInputs.FirstOrDefault();
            }

            _updatingInputLists = false;
            var selectedTitle = TitleBox.SelectedItem as VmixInput;
            _selectedTitleKey = selectedTitle?.Key;
            _vmixTitleKey = _selectedTitleKey;
            TitleStatusText.Text = _selectedTitleKey == null ? "Select subtitle title" : $"Subtitle: {selectedTitle?.Title}";

            _updatingInputLists = true;
            try
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    var slot = _slots[i];
                    var selectedKey = _vmixSlotInputKeys.GetValueOrDefault(slot.Number);

                    slot.InputBox!.ItemsSource = videoInputs;
                    var restored = string.IsNullOrWhiteSpace(selectedKey) ? null : videoInputs.FirstOrDefault(x =>
                        x.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
                    slot.InputBox.SelectedItem = restored;
                    slot.Input = restored;
                    slot.InputKey = restored?.Key ?? selectedKey;
                    _vmixSlotInputKeys[slot.Number] = restored?.Key ?? selectedKey;
                    UpdateSlotStatus(slot);
                }
            }
            finally
            {
                _updatingInputLists = false;
            }

            InvalidateSubtitle();
            ConnectionText.Text = "Connected to vMix";
            ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(97, 157, 118));
            ApiStatusText.Text = _apiBase;
            GlobalStatus.Text = $"Ready — {_inputs.Count} inputs detected";
        }
        catch (Exception ex)
        {
            SetDisconnected("Connection failed");
            GlobalStatus.Text = "Connection failed: " + ex.Message;
        }
    }

    private void SetDisconnected(string message)
    {
        _retryAfter = DateTime.UtcNow.AddSeconds(1);
        InvalidateSubtitle();
        ConnectionText.Text = message;
        ConnectionDot.Fill = Brushes.Red;
        ApiStatusText.Text = "vMix API could not be found automatically";
        GlobalStatus.Text = message;
    }

    private async Task ConnectObsAsync(string host, int port, string password)
    {
        if (_refreshing) throw new InvalidOperationException("Başka bir bağlantı işlemi sürüyor.");
        host = host.Trim().Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) host = "127.0.0.1";
        if (!IPAddress.TryParse(host, out var obsAddress) || obsAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("OBS adresi, aynı yerel ağdaki IPv4 adresi olmalı (ör. 192.168.1.20).");
        if (!IPAddress.IsLoopback(obsAddress) && !GetSubnetCandidates().Any(url =>
                string.Equals(new Uri(url).Host, obsAddress.ToString(), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Bu adres bilgisayarınızla aynı küçük yerel ağda görünmüyor. OBS’yi aynı ağa bağlayıp Yerel ağda OBS ara düğmesini kullanın.");
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "Port 1–65535 arasında olmalı.");
        if (password.Length > 4096) throw new ArgumentException("OBS parolası çok uzun.");

        _refreshing = true;
        ObsWebSocketClient? candidate = null;
        try
        {
            _obsStatus = $"{host}:{port} adresine bağlanılıyor…";
            SendWebState(true);
            candidate = await ObsWebSocketClient.ConnectAsync(new UriBuilder("ws", host, port).Uri, password, _lifetime.Token);
            var inputs = await candidate.GetInputsAsync(_lifetime.Token);
            if (inputs.Count == 0) throw new InvalidOperationException("OBS bağlantısı açıldı fakat kaynak listesi boş.");

            await ClearCurrentOutputTextAsync();
            SaveCurrentOutputMappings();
            var previous = _obsClient;
            _obsClient = candidate;
            candidate = null;
            _obsHost = host;
            _obsPort = port;
            _connectedObsHost = host;
            _connectedObsPort = port;
            _outputMode = "obs";
            await ApplyObsInputsAsync(inputs);
            _obsInputNames.Clear();
            foreach (var input in inputs) _obsInputNames.Add(input.Name);
            _outputMode = "obs";
            _subtitleOutput = new ObsSubtitleOutput(_obsClient, () => _selectedTitleKey);
            var hasTextSource = inputs.Any(input => input.Kind.StartsWith("text_", StringComparison.OrdinalIgnoreCase));
            var hasMediaSource = inputs.Any(input => input.Kind is "ffmpeg_source" or "vlc_source");
            _obsStatus = $"OBS bağlı • {host}:{port}" +
                (hasTextSource ? "" : " • OBS'de önce bir Text kaynağı oluşturun") +
                (hasMediaSource ? "" : " • Oynatım takibi için OBS medya kaynağı bulunamadı");
            InvalidateSubtitle();
            if (previous != null) await previous.DisposeAsync();
            GlobalStatus.Text = "OBS bağlantısı hazır. Aktif sahnedeki seçili medya kaynağı takip ediliyor.";
        }
        catch (Exception ex)
        {
            _obsStatus = "OBS bağlantısı kurulamadı: " + ex.Message;
            throw;
        }
        finally
        {
            if (candidate != null) await candidate.DisposeAsync();
            _refreshing = false;
            SendWebState(true);
        }
    }

    private async Task RefreshObsInputsAsync()
    {
        if (_obsClient?.IsConnected != true) throw new InvalidOperationException("Önce OBS’ye bağlanın.");
        if (_refreshing) throw new InvalidOperationException("Başka bir bağlantı işlemi sürüyor.");
        _refreshing = true;
        try
        {
            var inputs = await _obsClient.GetInputsAsync(_lifetime.Token);
            await ApplyObsInputsAsync(inputs);
            _obsInputNames.Clear();
            foreach (var input in inputs) _obsInputNames.Add(input.Name);
            GlobalStatus.Text = $"OBS kaynakları yenilendi • {inputs.Count} kaynak";
        }
        finally { _refreshing = false; SendWebState(true); }
    }

    private Task ApplyObsInputsAsync(IReadOnlyList<ObsInput> inputs)
    {
        _inputs = inputs.Select(input => new VmixInput(
                input.Name,
                "",
                input.Kind is "ffmpeg_source" or "vlc_source" ? "Video" : input.Kind,
                input.Name,
                ""))
            .ToList();
        var videoInputs = _inputs.Where(input => input.Type == "Video").ToList();
        var textInputs = _inputs.Where(input => input.Type.StartsWith("text_", StringComparison.OrdinalIgnoreCase)).ToList();

        _updatingInputLists = true;
        try
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.InputBox!.ItemsSource = videoInputs;
                var selectedKey = _obsSlotInputKeys.GetValueOrDefault(slot.Number);
                var restored = videoInputs.FirstOrDefault(input => input.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
                slot.InputBox.SelectedItem = restored;
                slot.Input = restored;
                slot.InputKey = restored?.Key;
                _obsSlotInputKeys[slot.Number] = restored?.Key ?? selectedKey;
                UpdateSlotStatus(slot);
            }

            TitleBox.ItemsSource = textInputs;
            TitleBox.SelectedItem = textInputs.FirstOrDefault(input => input.Key == _obsTitleKey)
                ?? textInputs.FirstOrDefault(input => input.Title.Contains("subtitle", StringComparison.OrdinalIgnoreCase))
                ?? textInputs.FirstOrDefault();
        }
        finally { _updatingInputLists = false; }
        var selectedTitle = TitleBox.SelectedItem as VmixInput;
        _selectedTitleKey = selectedTitle?.Key;
        _obsTitleKey = _selectedTitleKey;
        TitleStatusText.Text = _selectedTitleKey == null ? "OBS text source not selected" : $"Subtitle: {selectedTitle?.Title}";
        InvalidateSubtitle();
        return Task.CompletedTask;
    }

    private async Task ScanObsNetworkAsync()
    {
        if (_refreshing || _obsScanning) throw new InvalidOperationException("Başka bir bağlantı işlemi sürüyor.");
        _obsScanning = true;
        _obsStatus = "Yerel ağda OBS aranıyor…";
        SendWebState(true);
        try
        {
            var hosts = GetSubnetCandidates().Select(url => new Uri(url).Host).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            using var gate = new SemaphoreSlim(32);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var found = new System.Collections.Concurrent.ConcurrentBag<string>();
            var tasks = hosts.Select(async address =>
            {
                await gate.WaitAsync(timeout.Token);
                try
                {
                    using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    probeTimeout.CancelAfter(TimeSpan.FromMilliseconds(350));
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(address, 4455, probeTimeout.Token);
                    if (probe.Connected) found.Add(address);
                }
                catch { }
                finally { gate.Release(); }
            }).ToArray();
            await Task.WhenAll(tasks);
            _obsFoundHosts = found.OrderBy(address => address, StringComparer.OrdinalIgnoreCase).ToList();
            _obsStatus = _obsFoundHosts.Count == 0
                ? "Yerel ağda 4455 portunda OBS bulunamadı. OBS WebSocket ayarını ve IP adresini kontrol edin."
                : $"{_obsFoundHosts.Count} olası OBS bilgisayarı bulundu.";
        }
        finally
        {
            _obsScanning = false;
            SendWebState(true);
        }
    }

    private async Task ClearSlotAsync(Slot slot)
    {
        CancelTranslation(slot);
        slot.RequestedLanguage = _preferences.TargetLanguage;
        slot.UseSourceSubtitles = false;
        slot.SourceLanguageCode = null;
        slot.SourceCues.Clear();
        slot.TranslationState = "";
        slot.HasManualEdits = false;
        slot.PreviewButton!.IsEnabled = false;
        slot.RetryButton!.IsEnabled = false;
        slot.Input = null;
        slot.InputKey = null;
        if(_outputMode=="obs")_obsSlotInputKeys[slot.Number]=null;else _vmixSlotInputKeys[slot.Number]=null;
        slot.InputBox!.SelectedItem = null;

        slot.SrtPath = null;
        slot.OutputPath = null;
        slot.SrtBox!.Text = "";
        slot.SrtBox.ToolTip = null;
        slot.Cues.Clear();
        slot.Current!.Text = "—";
        slot.Status!.Text = "Not configured";
        slot.Status.Foreground = new SolidColorBrush(Color.FromRgb(153, 156, 161));

        InvalidateSubtitle();
        await Task.CompletedTask;
    }

    private async Task BrowseSrtAsync(Slot slot)
    {
        var dialog = new OpenFileDialog { Filter = "Subtitle files (*.srt)|*.srt", Title = $"Select SRT — Slot {slot.Number:00}" };
        if (dialog.ShowDialog(this) != true) return;
        List<SubtitleCue> source;
        try { source = SrtParser.Parse(File.ReadAllText(dialog.FileName, Encoding.UTF8)); }
        catch (Exception ex)
        {
            StudioDialog.Show(this, ex.Message + "\nÖnceki dosya korundu.", "SRT hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        CancelTranslation(slot);
        slot.SourceCues = source;
        slot.SourceLanguageCode = DetectSourceLanguageCode(source);
        slot.Cues = new(); // Never send the untranslated source to air.
        slot.HasManualEdits = false;
        slot.SrtPath = dialog.FileName;
        slot.OutputPath = null;
        slot.SrtBox!.Text = Path.GetFileName(dialog.FileName);
        slot.SrtBox.ToolTip = dialog.FileName;
        slot.Current!.Text = "—";
        InvalidateSubtitle();
        if(slot.UseSourceSubtitles){UseOriginalSubtitles(slot);if(_detailSlot==slot)LoadDetail();return;}
        var version = slot.TranslationVersion;
        slot.TranslationState = "ChatGPT bağlantısı bekleniyor";
        slot.PreviewButton!.IsEnabled = false;
        slot.RetryButton!.IsEnabled = true;
        UpdateSlotStatus(slot);
        if (!await EnsureChatGptConnectedAsync())
        {
            if (!_closed && version == slot.TranslationVersion)
            { slot.TranslationState = "ChatGPT bağlantısı gerekiyor • Çevir ile deneyin"; UpdateSlotStatus(slot); }
            return;
        }
        if (_closed || version != slot.TranslationVersion) return;
        await TranslateSlotAsync(slot);
    }

    private static double? ParseVideoMilliseconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var milliseconds) && double.IsFinite(milliseconds))
            return Math.Max(0, milliseconds);
        return TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var time)
            ? Math.Max(0, time.TotalMilliseconds) : null;
    }

    private static string? DetectSourceLanguageCode(IEnumerable<SubtitleCue> cues)
    {
        var text = string.Join(" ", cues.Select(cue => cue.Text));
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Any(c => c is >= '\u3040' and <= '\u30ff')) return "JA";
        if (text.Any(c => c is >= '\uac00' and <= '\ud7af')) return "KO";
        if (text.Any(c => c is >= '\u4e00' and <= '\u9fff')) return "CN";
        if (text.Any(c => c is >= '\u0600' and <= '\u06ff')) return "AR";
        if (text.Any(c => c is >= '\u0370' and <= '\u03ff')) return "EL";
        if (text.Any(c => c is >= '\u0590' and <= '\u05ff')) return "HE";
        if (text.Any(c => c is >= '\u0400' and <= '\u04ff'))
            return text.IndexOfAny(new[] { 'і', 'ї', 'є', 'ґ', 'І', 'Ї', 'Є', 'Ґ' }) >= 0 ? "UK" : "RU";
        if (text.IndexOfAny(new[] { 'ı', 'İ', 'ğ', 'Ğ', 'ş', 'Ş' }) >= 0) return "TUR";
        if (text.IndexOfAny(new[] { 'ą', 'ę', 'ł', 'ń', 'ś', 'ź', 'ż', 'Ą', 'Ę', 'Ł', 'Ń', 'Ś', 'Ź', 'Ż' }) >= 0) return "PL";
        if (text.IndexOfAny(new[] { 'ñ', 'Ñ', '¿', '¡' }) >= 0) return "ES";
        if (text.IndexOfAny(new[] { 'ã', 'õ', 'Ã', 'Õ' }) >= 0) return "PT";
        if (text.IndexOf('ß') >= 0 || text.IndexOf('ẞ') >= 0) return "DE";

        var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"[\p{L}]+")
            .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var vocabularies = new (string Code, string[] Words)[]
        {
            ("FR", new[] { "le", "les", "des", "une", "et", "est", "je", "vous", "nous", "pas", "dans", "pour", "avec", "qui" }),
            ("ES", new[] { "los", "las", "una", "y", "está", "que", "por", "para", "pero", "con", "del", "como" }),
            ("DE", new[] { "der", "die", "das", "ein", "eine", "und", "ist", "nicht", "ich", "wir", "mit", "für" }),
            ("PT", new[] { "os", "as", "uma", "não", "você", "para", "com", "está", "são", "mas" }),
            ("IT", new[] { "gli", "che", "non", "sono", "con", "per", "una", "questo", "della", "alla" }),
            ("NL", new[] { "het", "een", "van", "en", "niet", "dat", "met", "voor", "zijn", "maar" }),
            ("EN", new[] { "the", "and", "you", "that", "are", "not", "this", "with", "have", "from", "what" })
        };
        var ranked = vocabularies.Select(item => (item.Code, Score: item.Words.Count(words.Contains)))
            .OrderByDescending(item => item.Score).ToArray();
        return ranked.Length > 0 && ranked[0].Score >= 3 && (ranked.Length == 1 || ranked[0].Score > ranked[1].Score)
            ? ranked[0].Code : null;
    }

    private void CancelTranslation(Slot slot)
    {
        slot.TranslationVersion++;
        slot.Editor?.MarkStale();
        slot.Editor = null;
        slot.TranslationCancellation?.Cancel();
        slot.TranslationCancellation = null;
        if (slot.RetryButton != null) slot.RetryButton.Content = "Çevir";
    }

    private void UseOriginalSubtitles(Slot slot)
    {
        CancelTranslation(slot);slot.Cues=slot.SourceCues.ToList();slot.OutputPath=null;slot.HasManualEdits=false;slot.TargetLanguage="Orijinal";
        slot.TranslationState=slot.Cues.Count>0?$"Orijinal hazır • {slot.Cues.Count} altyazı":"SRT yükleyin";
        slot.PreviewButton!.IsEnabled=slot.Cues.Count>0;slot.RetryButton!.IsEnabled=slot.SourceCues.Count>0;
        slot.SrtBox!.Text=System.IO.Path.GetFileName(slot.SrtPath??"");slot.SrtBox.ToolTip=slot.SrtPath;UpdateSlotStatus(slot);InvalidateSubtitle();
    }
    private async Task TranslateSlotAsync(Slot slot)
    {
        if (slot.SourceCues.Count == 0 || _closed) return;
        CancelTranslation(slot);
        var version = slot.TranslationVersion;
        using var cancellation = new CancellationTokenSource();
        slot.TranslationCancellation = cancellation;
        var source = slot.SourceCues.ToArray();
        var model = (ChatGptModelBox.SelectedItem as ChatGptModel)?.Slug ?? "";
        var targetLanguage = slot.RequestedLanguage ?? _preferences.TargetLanguage;
        slot.Status!.ToolTip = null;
        slot.TranslationState = "Çeviri sırasında — bekliyor";
        slot.RetryButton!.Content = "İptal";
        slot.RetryButton.IsEnabled = true;
        slot.PreviewButton!.IsEnabled = false;
        _usageStatus = "Çeviri sürüyor";
        _lastTranslationActivity = DateTime.Now;
        UpdateSlotStatus(slot);
        try
        {
            var translated = await _translator.TranslateAsync(source, model, targetLanguage, (done, total) =>
            {
                if (_closed || version != slot.TranslationVersion) return;
                slot.TranslationState = $"{targetLanguage} • {done}/{total} çevriliyor";
                UpdateSlotStatus(slot);
            }, cancellation.Token, _glossaryEntries.ToArray(),_preferences.TranslationPreset,slot.SourceLanguageCode??"auto");
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || version != slot.TranslationVersion) return;
            slot.Cues = translated;
            _sessionTranslations++;
            _sessionTranslatedCues += translated.Count;
            _usageStatus = $"Son çeviri tamamlandı • {_translator.LastCacheHits}/{translated.Count} önbellekten";
            _lastTranslationActivity = DateTime.Now;
            slot.TargetLanguage = targetLanguage;
            slot.HasManualEdits = false;
            slot.TranslationState = $"{targetLanguage} hazır • {translated.Count} altyazı";
            try
            {
                slot.OutputPath = TranslatedSubtitleStore.Save(targetLanguage, translated);
                slot.SrtBox!.Text = Path.GetFileName(slot.OutputPath);
                slot.SrtBox.ToolTip = $"Kaynak: {slot.SrtPath}\nÇevrilmiş SRT: {slot.OutputPath}";
            }
            catch (Exception ex)
            {
                slot.OutputPath = null;
                slot.Status!.ToolTip = "Çeviri hazır; SRT dosyası otomatik kaydedilemedi: " + ex.Message;
                slot.TranslationState += " • dosyayı Preview'dan kaydedin";
            }
            InvalidateSubtitle();
        }
        catch (OperationCanceledException)
        {
            if (!_closed && version == slot.TranslationVersion)
                slot.TranslationState = cancellation.IsCancellationRequested ? "Çeviri iptal edildi" : "Çeviri zaman aşımı — yeniden deneyin";
        }
        catch (Exception ex)
        {
            _usageStatus = ex.Message.Contains("kullanım limitine ulaşıldı", StringComparison.OrdinalIgnoreCase)
                ? "ChatGPT kullanım sınırı bildirildi"
                : "Son çeviri tamamlanamadı";
            _lastTranslationActivity = DateTime.Now;
            if (!_closed && version == slot.TranslationVersion)
            {
                slot.TranslationState = "Çeviri başarısız — yeniden deneyin";
                slot.Status!.ToolTip = ex.Message;
                StudioDialog.Show(this, ex.Message + "\nChatGPT bağlantısını ve kullanım hakkını kontrol edin. Çeviri tamamlanana kadar kaynak metin yayına gönderilmez.",
                    "ChatGPT çeviri hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (!_closed && version == slot.TranslationVersion)
            {
                slot.TranslationCancellation = null;
                slot.RetryButton!.Content = "Çevir";
                slot.RetryButton.IsEnabled = slot.SourceCues.Count > 0;
                slot.PreviewButton!.IsEnabled = slot.Cues.Count > 0;
                UpdateSlotStatus(slot);
            }
        }
    }


    private void UpdateSlotStatus(Slot slot)
    {
        if (slot.Status == null) return;
        slot.Status.Text = slot.TranslationState.Length > 0 ? slot.TranslationState :
            slot.Cues.Count > 0 ? $"● Hazır · {slot.Cues.Count}" : "● Bekliyor";
        slot.Status.Foreground = slot.TranslationCancellation != null ? Brushes.Gold :
            slot.Cues.Count > 0 ? new SolidColorBrush(Color.FromRgb(137, 181, 151)) : new SolidColorBrush(Color.FromRgb(153, 156, 161));
    }

    private void OpenPreview(Slot slot)
    {
        if (slot.Cues.Count == 0) return;
        if (slot.Editor != null) { slot.Editor.Activate(); return; }
        var version = slot.TranslationVersion;
        var editor = new SubtitlePreviewWindow(slot.Number, slot.SrtPath ?? "subtitles.srt", slot.SourceCues, slot.Cues, slot.TargetLanguage,
            corrected =>
            {
                if (_closed || version != slot.TranslationVersion || slot.TranslationCancellation != null) return false;
                slot.Cues = corrected;
                slot.HasManualEdits = true;
                slot.TranslationState = $"Düzeltilmiş {slot.TargetLanguage} • {corrected.Count} altyazı";
                InvalidateSubtitle();
                UpdateSlotStatus(slot);
                return true;
            }) { Owner = this };
        slot.Editor = editor;
        editor.Closed += (_, _) => { if (ReferenceEquals(slot.Editor, editor)) slot.Editor = null; };
        editor.Show();
    }

    private async Task TickAsync()
    {
        if (_outputMode == "obs")
        {
            await TickObsAsync();
            return;
        }
        if (_closed || _tickRunning || _refreshing || string.IsNullOrWhiteSpace(_apiBase) || DateTime.UtcNow < _retryAfter) return;
        _tickRunning = true;
        var snapshotRevision = _revision;

        try
        {
            var xml = await _http.GetStringAsync(_apiBase);
            if (_closed || snapshotRevision != _revision || _refreshing) return;
            var doc = XDocument.Parse(xml);
            UpdateVmixMediaInformation(doc);

            var activeNode = doc.Descendants("active").FirstOrDefault();
            var activeNumber = activeNode?.Value?.Trim();
            var activeElement = doc.Descendants("input").FirstOrDefault(x => (string?)x.Attribute("number") == activeNumber);
            var activeInput = activeElement == null ? null : new VmixInput(
                (string?)activeElement.Attribute("key") ?? "", activeNumber ?? "",
                (string?)activeElement.Attribute("type") ?? "", (string?)activeElement.Attribute("title") ?? "",
                (string?)activeElement.Attribute("state") ?? "");
            _videoSource = activeInput?.Title ?? "";
            _videoIsPlaying = activeInput?.State.Equals("Running",StringComparison.OrdinalIgnoreCase) == true || activeInput?.State.Equals("Playing",StringComparison.OrdinalIgnoreCase) == true;
            _videoPositionMilliseconds = ParseVideoMilliseconds((string?)activeElement?.Attribute("position"));
            var vmixDuration = ParseVideoMilliseconds((string?)activeElement?.Attribute("duration") ?? (string?)activeElement?.Attribute("length"));
            _videoDurationMilliseconds = vmixDuration is > 0 ? vmixDuration : null;
            if (await AdvancePlaylistIfFinishedAsync(activeInput?.Key,_videoPositionMilliseconds,_videoDurationMilliseconds)) return;
            ConnectionText.Text = "Connected to vMix";
            ConnectionDot.Fill = new SolidColorBrush(Color.FromRgb(97, 157, 118));
            foreach (var title in _titlesToClear.ToArray())
            {
                if (!doc.Descendants("input").Any(n => string.Equals((string?)n.Attribute("key"), title, StringComparison.OrdinalIgnoreCase)))
                { _titlesToClear.Remove(title); continue; }
                if (await PublishSubtitleAsync("", title)) _titlesToClear.Remove(title);
                else return;
            }

            if (_closed || snapshotRevision != _revision || _refreshing) return;
            ProgramText.Text = activeInput == null
                ? $"PROGRAM: {activeNumber ?? "—"}"
                : $"PROGRAM: {activeInput.Number} — {activeInput.Title}";

            var previewNumber = doc.Descendants("preview").FirstOrDefault()?.Value?.Trim();
            var previewElement = doc.Descendants("input").FirstOrDefault(x => (string?)x.Attribute("number") == previewNumber);
            _vmixProgramInputKey = activeInput?.Key;
            _vmixPreviewInputKey = (string?)previewElement?.Attribute("key");

            var slot = _slots.FirstOrDefault(s => s.InputKey != null && s.InputKey == activeInput?.Key);

            foreach (var s in _slots)
            {
                if (s != slot && s.Status != null && s.InputKey != null)
                { UpdateSlotStatus(s); s.Current!.Text = "—"; }
            }

            if (slot == null || slot.Cues.Count == 0 || slot.Input == null)
            {
                await SynchronizeTextAsync("", activeInput?.Key);
                return;
            }

            var node = doc.Descendants("input")
                .FirstOrDefault(x => string.Equals((string?)x.Attribute("key"), slot.Input.Key, StringComparison.OrdinalIgnoreCase));

            if (node == null) { await SynchronizeTextAsync("", activeInput?.Key); return; }

            var positionRaw = (string?)node.Attribute("position") ?? "0";
            if (!double.TryParse(positionRaw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var ms))
                return;

            var cue = slot.Cues.FirstOrDefault(c =>
                ms >= c.Start.TotalMilliseconds && ms < c.End.TotalMilliseconds);

            var text = cue?.Text ?? "";
            slot.Current!.Text = string.IsNullOrWhiteSpace(text) ? "—" : text;
            slot.Status!.Text = $"ON AIR  {TimeSpan.FromMilliseconds(ms):mm\\:ss\\.fff}\n{slot.TranslationState}";
            slot.Status.Foreground = Brushes.OrangeRed;

            await SynchronizeTextAsync(text, slot.Input.Key);
        }
        catch (Exception ex)
        {
            if (!_closed) { SetDisconnected("Connection lost — retrying"); GlobalStatus.Text = ex.Message; }
        }
        finally { _tickRunning = false; }
    }

    private async Task TickObsAsync()
    {
        if (_closed || _tickRunning || _refreshing || _obsClient?.IsConnected != true || DateTime.UtcNow < _retryAfter) return;
        _tickRunning = true;
        var snapshotRevision = _revision;
        try
        {
            var scene = await _obsClient.GetCurrentProgramSceneAsync(_lifetime.Token);
            if (_closed || snapshotRevision != _revision || _refreshing) return;
            _vmixProgramInputKey = null;
            _vmixPreviewInputKey = null;
            ProgramText.Text = string.IsNullOrWhiteSpace(scene) ? "PROGRAM: —" : "PROGRAM: " + scene;
            var sceneItems = await _obsClient.GetSceneItemsAsync(scene, _lifetime.Token);

            foreach (var title in _titlesToClear.ToArray())
            {
                if (!_obsInputNames.Contains(title)) { _titlesToClear.Remove(title); continue; }
                if (await PublishSubtitleAsync("", title)) _titlesToClear.Remove(title);
                else return;
            }

            if (_closed || snapshotRevision != _revision || _refreshing) return;
            var activeNames = sceneItems.Where(item => item.Enabled).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var slot = _slots.FirstOrDefault(candidate => candidate.InputKey != null && activeNames.Contains(candidate.InputKey));
            _videoSource = slot?.Input?.Title ?? "";
            _videoPositionMilliseconds = null;
            _videoDurationMilliseconds = null;
            _videoIsPlaying = false;
            ObsMediaStatus? playlistMedia = null;
            var currentPlaylistKey = _playingPlaylistId != null && _playlistIndex >= 0 && _playlistIndex < _playlistQueue.Count
                ? _playlistQueue[_playlistIndex] : null;
            if (currentPlaylistKey != null && activeNames.Contains(currentPlaylistKey))
            {
                playlistMedia = await _obsClient.GetMediaStatusAsync(currentPlaylistKey,_lifetime.Token);
                StoreObsMediaDuration(currentPlaylistKey,playlistMedia);
                _videoIsPlaying = playlistMedia.State.EndsWith("PLAYING",StringComparison.OrdinalIgnoreCase);
                _videoSource = _inputs.FirstOrDefault(input => input.Key.Equals(currentPlaylistKey,StringComparison.OrdinalIgnoreCase))?.Title ?? currentPlaylistKey;
                _videoPositionMilliseconds = playlistMedia.PositionMilliseconds;
                _videoDurationMilliseconds = playlistMedia.DurationMilliseconds is > 0 ? playlistMedia.DurationMilliseconds : null;
                if (await AdvancePlaylistIfFinishedAsync(currentPlaylistKey,_videoPositionMilliseconds,_videoDurationMilliseconds,playlistMedia.State.Contains("ENDED",StringComparison.OrdinalIgnoreCase))) return;
            }
            foreach (var other in _slots)
            {
                if (other != slot && other.Status != null && other.InputKey != null)
                { UpdateSlotStatus(other); other.Current!.Text = "—"; }
            }

            if (slot == null || slot.Input == null)
            {
                await SynchronizeTextAsync("", currentPlaylistKey ?? scene);
                return;
            }

            var media = playlistMedia ?? await _obsClient.GetMediaStatusAsync(slot.InputKey!, _lifetime.Token);
            if (_closed || snapshotRevision != _revision || _refreshing) return;
            StoreObsMediaDuration(slot.InputKey!,media);
            _videoIsPlaying = media.State.EndsWith("PLAYING",StringComparison.OrdinalIgnoreCase);
            _videoPositionMilliseconds = media.PositionMilliseconds;
            _videoDurationMilliseconds = media.DurationMilliseconds is > 0 ? media.DurationMilliseconds : null;
            if (media.PositionMilliseconds is not double position ||
                !(media.State.EndsWith("PLAYING", StringComparison.OrdinalIgnoreCase) ||
                  media.State.EndsWith("PAUSED", StringComparison.OrdinalIgnoreCase)))
            {
                slot.Current!.Text = "—";
                slot.Status!.Text = "Video bekleniyor\n" + slot.TranslationState;
                await SynchronizeTextAsync("", slot.InputKey);
                return;
            }

            var playbackTime = TimeSpan.FromMilliseconds(Math.Max(0, position)).ToString(@"mm\:ss\.fff");
            var duration = media.DurationMilliseconds is double total
                ? TimeSpan.FromMilliseconds(Math.Max(0, total)).ToString(@"mm\:ss\.fff")
                : "—";
            slot.Status!.Text = $"ON AIR  {playbackTime} / {duration}\n{slot.TranslationState}";
            slot.Status.Foreground = Brushes.OrangeRed;
            if (slot.Cues.Count == 0)
            {
                slot.Current!.Text = "—";
                await SynchronizeTextAsync("", slot.InputKey);
                return;
            }

            var cue = slot.Cues.FirstOrDefault(item =>
                position >= item.Start.TotalMilliseconds && position < item.End.TotalMilliseconds);
            var text = cue?.Text ?? "";
            slot.Current!.Text = string.IsNullOrWhiteSpace(text) ? "—" : text;
            await SynchronizeTextAsync(text, slot.InputKey);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closed)
            {
                _obsStatus = "OBS bağlantısı kesildi: " + ex.Message;
                _retryAfter = DateTime.UtcNow.AddSeconds(1);
                GlobalStatus.Text = _obsStatus;
                SendWebState(true);
            }
        }
        finally { _tickRunning = false; }
    }

    private async Task SynchronizeTextAsync(string text, string? programKey)
    {
        if (_lastProgramInput == programKey && _lastSentText == text) return;
        var revision = _revision;
        if (await PublishSubtitleAsync(text) && revision == _revision)
        {
            _lastProgramInput = programKey;
            _lastSentText = text;
        }
    }

    private async Task<bool> PublishSubtitleAsync(string value, string? titleKey = null)
    {
        if (_closed) return false;
        try
        {
            return await _subtitleOutput.PublishAsync(value, titleKey, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            if (!_closed) { GlobalStatus.Text = $"Subtitle send failed — retrying: {ex.Message}"; _retryAfter = DateTime.UtcNow.AddSeconds(1); }
            return false;
        }
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        InvalidateSubtitle();

        // Clear every configured video input and SRT file.
        foreach (var slot in _slots)
        {
            CancelTranslation(slot);
            slot.SourceLanguageCode = null;
            slot.SourceCues.Clear();
            slot.TranslationState = "";
            slot.HasManualEdits = false;
            slot.PreviewButton!.IsEnabled = false;
            slot.RetryButton!.IsEnabled = false;
            slot.Input = null;
            slot.InputKey = null;
            if (slot.InputBox != null)
                slot.InputBox.SelectedItem = null;

            slot.SrtPath = null;
            slot.OutputPath = null;
            if (slot.SrtBox != null)
            {
                slot.SrtBox.Text = "";
                slot.SrtBox.ToolTip = null;
            }

            slot.Cues.Clear();
            if (slot.Current != null)
                slot.Current.Text = "—";
            if (slot.Status != null)
            {
                slot.Status.Text = "Not configured";
                slot.Status.Foreground = new SolidColorBrush(Color.FromRgb(153, 156, 161));
            }
        }

        InvalidateSubtitle();
        await Task.CompletedTask;
        GlobalStatus.Text = "All slots cleared";
    }
}

public sealed class Slot
{
    public int Number { get; set; }
    public string? InputKey { get; set; }
    public VmixInput? Input { get; set; }
    public string? SrtPath { get; set; }
    public string? OutputPath { get; set; }
    public List<SubtitleCue> Cues { get; set; } = new();
    public List<SubtitleCue> SourceCues { get; set; } = new();
    public string? SourceLanguageCode { get; set; }
    public CancellationTokenSource? TranslationCancellation { get; set; }
    public int TranslationVersion { get; set; }
    public string TranslationState { get; set; } = "";
    public string TargetLanguage { get; set; } = "Türkçe";
    public string? RequestedLanguage { get; set; }
    public bool UseSourceSubtitles {get;set;}
    public bool HasManualEdits { get; set; }
    public Button? PreviewButton { get; set; }
    public Button? RetryButton { get; set; }
    public SubtitlePreviewWindow? Editor { get; set; }
    public ComboBox? InputBox { get; set; }
    public TextBox? SrtBox { get; set; }
    public TextBlock? Status { get; set; }
    public TextBlock? Current { get; set; }
    public Slot(int number) => Number = number;
}

public record VmixInput(string Key, string Number, string Type, string Title, string State)
{
    public string Display => string.IsNullOrWhiteSpace(Number) ? Title : $"{Number} — {Title}";

    // Also override ToString so the custom dark ComboBox template always
    // shows the friendly vMix input name instead of the record type/fields.
    public override string ToString() => Display;
}

