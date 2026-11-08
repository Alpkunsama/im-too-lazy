using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using VmixSubtitleController.Output;
namespace VmixSubtitleController;
public partial class MainWindow
{
    private bool _webReady;
    private bool _startupGate = true;
    private bool _startupOutputChosen;
    private string? _lastWebState;
    private async Task InitializeWebAsync()
    {
        string root;
        try
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"im-too-lazy","ui",typeof(MainWindow).Module.ModuleVersionId.ToString());
            Directory.CreateDirectory(root);
            foreach (var name in new[]{"index.html","app.js","app.css","brand-cat.png"})
            {
                var resource = Application.GetResourceStream(new Uri("WebUI/dist/"+name,UriKind.Relative)) ?? throw new FileNotFoundException("Arayüz kaynağı bulunamadı: "+name);
                using var input = resource.Stream;
                using var output = File.Create(Path.Combine(root,name));
                input.CopyTo(output);
            }
        }
        catch (Exception ex)
        {
            if (!_closed) ShowWebFailure("Arayüz dosyaları hazırlanamadı.\n" + ex.Message);
            return;
        }
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"im-too-lazy","ui-browser"));
            if (_closed) return;
            await WebSurface.EnsureCoreWebView2Async(environment);
            if (_closed) return;
            WebSurface.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255,20,22,25);
            var core = WebSurface.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping("im-too-lazy.example",root,CoreWebView2HostResourceAccessKind.DenyCors);
            core.NewWindowRequested += (_,e) => e.Handled = true;
            core.PermissionRequested += (_,e) => e.State = e.PermissionKind == CoreWebView2PermissionKind.Camera ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            core.NavigationStarting += (_,e) => { if (!IsLocalWebUri(e.Uri)) e.Cancel = true; };
            core.WebMessageReceived += WebMessageReceived;
            core.NavigationCompleted += (_,e) => { if (!e.IsSuccess) ShowWebFailure("Arayüz yüklenemedi: "+e.WebErrorStatus); };
            UpdateWindowChrome();
            core.Navigate("https://im-too-lazy.example/index.html");
            _dashboardTimer.Tick += (_,_) => SendWebState();
        }
        catch (Exception ex) { if (!_closed) ShowWebFailure("WebView2 arayüzü açılamadı. Microsoft Edge WebView2 Runtime kurulumunu kontrol edin.\n"+ex.Message); }
    }
    private static bool IsLocalWebUri(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme == "https" && uri.Host == "im-too-lazy.example";
    private void ShowWebFailure(string text) { WebFailure.Visibility = Visibility.Visible; WebFailureText.Text = text; }
    private async void RetryWeb_Click(object sender,RoutedEventArgs e) { WebFailure.Visibility = Visibility.Collapsed; if (WebSurface.CoreWebView2 != null) WebSurface.CoreWebView2.Reload(); else await InitializeWebAsync(); }
    private void WebMessageReceived(object? sender,CoreWebView2WebMessageReceivedEventArgs e) => _ = HandleWebMessageAsync(e);
    private async Task HandleWebMessageAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? id = null;
        try
        {
            if (_closed || !IsLocalWebUri(e.Source)) return;
            var json = e.WebMessageAsJson;
            using var message = JsonDocument.Parse(json);
            var m = message.RootElement; id = m.GetProperty("id").GetString();
            if (json.Length > 3_000_000) throw new InvalidOperationException("İşlem için gönderilen veri çok büyük.");
            var command = m.GetProperty("command").GetString();
            object? commandResult = null;
            string Text(string key) => m.TryGetProperty(key,out var value) ? value.GetString() ?? "" : "";
            int Number(string key) => m.GetProperty(key).GetInt32();
            if (_startupGate && command is not ("ready" or "login" or "chooseOutputMode" or "startupContinue" or "skipLogin")) throw new InvalidOperationException("Önce OBS veya vMix seçin, ardından giriş ekranını geçin.");
            if (_refreshing && command?.StartsWith("playlist",StringComparison.Ordinal) == true) throw new InvalidOperationException("Video geçişi tamamlanana kadar bekleyin.");
            switch (command)
            {
                case "ready": _webReady = true; WebFailure.Visibility = Visibility.Collapsed; break;
                case "checkUpdates": _ = CheckForUpdateAsync(manual: true); break;
                case "showAvailableUpdate": _ = CheckForUpdateAsync(manual: true, knownRelease: _availableUpdate); break;
                case "chooseOutputMode":
                    if (Text("target") is not ("obs" or "vmix")) throw new ArgumentException("OBS veya vMix seçin.");
                    if (_refreshing) throw new InvalidOperationException("Bağlantı işlemi sürerken yayın sistemi değiştirilemez.");
                    var requestedOutputMode = Text("target");
                    if (_startupOutputChosen && requestedOutputMode != _outputMode)
                    {
                        ResetPlaylistPlayback();
                        _refreshing = true;
                        try
                        {
                            await ClearCurrentOutputTextAsync();
                            SaveCurrentOutputMappings();
                        }
                        finally { _refreshing = false; }
                    }
                    if (_outputMode != requestedOutputMode) { _broadcastCardId = null; _broadcastCardInputKey = null; }
                    _outputMode = requestedOutputMode;
                    if (!_playlists.Any(list => list.Id == _selectedPlaylistId && list.OutputMode == _outputMode)) _selectedPlaylistId = _playlists.FirstOrDefault(list => list.OutputMode == _outputMode)?.Id;
                    _startupOutputChosen = true;
                    if (_outputMode == "obs" && _obsClient?.IsConnected == true)
                    {
                        _subtitleOutput = new ObsSubtitleOutput(_obsClient,() => _selectedTitleKey);
                        await RefreshObsInputsAsync();
                    }
                    else if (_outputMode == "vmix" && !string.IsNullOrWhiteSpace(_apiBase))
                    {
                        _subtitleOutput = _vmixSubtitleOutput;
                        await RefreshInputsAsync();
                    }
                    InvalidateSubtitle();
                    break;
                case "page": ShowPage(Text("page") is "home" or "translate" or "dictionary" or "broadcast" or "profiles" or "slots" or "editor" or "connections" or "settings" ? Text("page") : "home"); if (_page == "settings") { SettingsLanguageBox.Text = _preferences.TargetLanguage; SettingsDarkBox.IsChecked = DarkModeBox.IsChecked; } break;
                case "connect": await ConnectVmixAddressAsync(Text("address")); break;
                case "scanVmix": await ScanVmixNetworkAsync(); break;
                case "refresh": Refresh_Click(this,new RoutedEventArgs()); break;
                case "disconnectVmix":
                    if (_refreshing) throw new InvalidOperationException("Bağlantı işlemi sürerken vMix bağlantısı kesilemez.");
                    _refreshing = true;
                    _playlistActive = false;
                    try
                    {
                        await ClearCurrentOutputTextAsync();
                        SaveCurrentOutputMappings();
                        _apiBase = null;
                        _vmixProgramInputKey = null;
                        _vmixPreviewInputKey = null;
                        ProgramText.Text = "PROGRAM: —";
                        ConnectionText.Text = "vMix bağlantısı kesildi";
                        ConnectionDot.Fill = Brushes.Gray;
                        ApiStatusText.Text = "Bağlantı kesildi";
                        GlobalStatus.Text = "vMix bağlantısı kesildi.";
                        InvalidateSubtitle();
                    }
                    finally { _refreshing = false; SendWebState(true); }
                    break;
                case "connectObs": await ConnectObsAsync(Text("host"),Number("port"),Text("password")); break;
                case "disconnectObs":
                    if (_refreshing) throw new InvalidOperationException("Bağlantı işlemi sürerken OBS bağlantısı kesilemez.");
                    _refreshing = true;
                    _playlistActive = false;
                    try
                    {
                        await ClearCurrentOutputTextAsync();
                        SaveCurrentOutputMappings();
                        var disconnectedClient = _obsClient;
                        _obsClient = null;
                        _obsInputNames.Clear();
                        _obsStatus = "OBS bağlantısı kesildi";
                        _vmixProgramInputKey = null;
                        _vmixPreviewInputKey = null;
                        _videoPositionMilliseconds = null;
                        _videoDurationMilliseconds = null;
                        _videoSource = "";
                        ProgramText.Text = "PROGRAM: —";
                        InvalidateSubtitle();
                        if (disconnectedClient != null) await disconnectedClient.DisposeAsync();
                        GlobalStatus.Text = "OBS bağlantısı kesildi.";
                    }
                    finally { _refreshing = false; SendWebState(true); }
                    break;
                case "refreshObs": await RefreshObsInputsAsync(); break;
                case "omtRefresh": commandResult = await RefreshOmtSourcesAsync(); break;
                case "omtStart": StartOmtReceiver(Text("address")); break;
                case "omtStop": StopOmtReceiver(); break;
                case "scanObs": await ScanObsNetworkAsync(); break;
                case "selectOutput":
                    if (_refreshing) throw new InvalidOperationException("Başka bir bağlantı işlemi sürüyor.");
                    _refreshing = true;
                    try
                    {
                        await ClearCurrentOutputTextAsync();
                        SaveCurrentOutputMappings();
                        if (Text("target") == "obs")
                        {
                            if (_obsClient?.IsConnected != true) throw new InvalidOperationException("Önce OBS’ye bağlanın.");
                            _outputMode = "obs";
                            _subtitleOutput = new ObsSubtitleOutput(_obsClient,() => _selectedTitleKey);
                            var inputs = await _obsClient.GetInputsAsync(_lifetime.Token);
                            await ApplyObsInputsAsync(inputs);
                            _obsInputNames.Clear();
                            foreach (var input in inputs) _obsInputNames.Add(input.Name);
                            _obsStatus = "OBS çıkışı aktif";
                        }
                        else if (Text("target") == "vmix")
                        {
                            if (string.IsNullOrWhiteSpace(_apiBase)) throw new InvalidOperationException("Önce vMix’e bağlanın.");
                            _outputMode = "vmix";
                            _subtitleOutput = _vmixSubtitleOutput;
                            await RefreshInputsAsync();
                        }
                        else throw new ArgumentException("Geçersiz çıkış türü.");
                    }
                    finally { _refreshing = false; }
                    InvalidateSubtitle();
                    break;
                case "startupContinue": if (_chatGptAccount.CanTranslate) _startupGate = false; else ChatGptConnect_Click(this,new RoutedEventArgs()); break;
                case "skipLogin": _signInCancellation?.Cancel(); _startupGate = false; break;
                case "cacheStats": commandResult=await Task.Run(TranslationCache.Stats);break;
                case "cacheClear": await Task.Run(TranslationCache.Clear);commandResult=await Task.Run(TranslationCache.Stats);break;
                case "translationPreset":
                    var preset=Text("preset");_=AppPreferences.PresetInstructions(preset);_preferences=_preferences with {TranslationPreset=preset};_preferences.Save();break;
                case "presetExample":
                    if(!_chatGptAccount.CanTranslate)throw new InvalidOperationException("Örneği çevirmek için ChatGPT hesabına bağlanın.");
                    var examplePreset=Text("preset");_=AppPreferences.PresetInstructions(examplePreset);
                    var exampleLanguage=Text("language");if(!AppPreferences.Languages.Contains(exampleLanguage))throw new ArgumentException("Örnek dilini seçin.");
                    commandResult=await _translator.TranslateTextAsync("Our team is ready to take on the challenge and win together.",(ChatGptModelBox.SelectedItem as ChatGptModel)?.Slug??"",exampleLanguage,_lifetime.Token,null,examplePreset);break;
                case "slotLanguage":
                    var languageSlot = _slots.First(x => x.Number == Number("slot"));
                    var requested = Text("language");
                    if (languageSlot.TranslationCancellation != null) throw new InvalidOperationException("Dil değiştirmek için önce çeviriyi iptal edin.");
                    if(requested=="__source__"){languageSlot.UseSourceSubtitles=true;UseOriginalSubtitles(languageSlot);if(_detailSlot==languageSlot)LoadDetail();break;}
                    if (!AppPreferences.Languages.Append(_preferences.TargetLanguage).Contains(requested)) throw new ArgumentException("Geçersiz çeviri dili.");
                    foreach(var item in _slots)item.RequestedLanguage??=_preferences.TargetLanguage;
                    if(languageSlot.UseSourceSubtitles){languageSlot.UseSourceSubtitles=false;languageSlot.Cues=new();languageSlot.OutputPath=null;languageSlot.TranslationState=languageSlot.SourceCues.Count>0?"SRT hazır • Çevir ile devam edin":"";languageSlot.PreviewButton!.IsEnabled=false;UpdateSlotStatus(languageSlot);InvalidateSubtitle();if(_detailSlot==languageSlot)LoadDetail();}
                    languageSlot.RequestedLanguage = requested; _preferences=_preferences with {TargetLanguage=requested};_preferences.Save();SettingsLanguageBox.Text=requested;break;
                case "glossarySet":
                    ReplaceGlossary(m.GetProperty("entries").EnumerateArray().Select(entry => new GlossaryEntry(
                        entry.GetProperty("source").GetString()?.Trim() ?? "",
                        entry.GetProperty("target").GetString()?.Trim() ?? "",
                        entry.GetProperty("language").GetString()?.Trim() ?? "")
                    {
                        Preset = entry.TryGetProperty("presetId",out var presetId) && presetId.ValueKind == JsonValueKind.String
                            ? new GlossaryPresetOrigin(presetId.GetString() ?? "",entry.GetProperty("presetLanguage").GetString() ?? "") : null
                    }).ToList());
                    break;
                case "glossaryAddPreset":
                {
                    var origin = new GlossaryPresetOrigin(Text("presetId"),Text("language").Trim());
                    origin.Validate();
                    var entries = _glossaryEntries.ToList();
                    var known = new HashSet<(string, string)>(entries.Select(entry => (entry.TargetLanguage,entry.SourceTerm)),StringTupleComparer.OrdinalIgnoreCase);
                    foreach (var term in m.GetProperty("terms").EnumerateArray())
                    {
                        var source = term.GetString()?.Trim() ?? "";
                        if (known.Add((origin.Language,source))) entries.Add(new GlossaryEntry(source,source,origin.Language) { Preset = origin });
                    }
                    var added = entries.Count - _glossaryEntries.Count;
                    if (added > 0) ReplaceGlossary(entries);
                    commandResult = new { added };
                    break;
                }
                case "glossaryRemovePreset":
                {
                    var origin = new GlossaryPresetOrigin(Text("presetId"),Text("language").Trim());
                    origin.Validate();
                    var entries = _glossaryEntries.Where(entry => entry.Preset != origin).ToList();
                    var removed = _glossaryEntries.Count - entries.Count;
                    if (removed > 0) ReplaceGlossary(entries);
                    commandResult = new { removed };
                    break;
                }
                case "changeAccount": await ChangeChatGptAccountAsync();break;
                case "outputTogglePause": await ToggleOutputPauseAsync();break;
                case "login": ChatGptConnect_Click(this,new RoutedEventArgs()); break;
                case "logout": ChatGptLogout_Click(this,new RoutedEventArgs()); break;
                case "models": ChatGptModels_Click(this,new RoutedEventArgs()); break;
                case "usage": ChatGptUsage_Click(this,new RoutedEventArgs()); break;
                case "model": ChatGptModelBox.SelectedItem = ChatGptModelBox.Items.Cast<ChatGptModel>().FirstOrDefault(x => x.Slug == Text("key")); break;
                case "title": TitleBox.SelectedItem = TitleBox.Items.Cast<VmixInput>().FirstOrDefault(x => x.Key == Text("key")); break;
                case "select": if (!SelectDetail(_slots.First(x => x.Number == Number("slot")))) throw new InvalidOperationException("Slot seçimi iptal edildi."); break;
                case "input":
                    var targetSlot = _slots.FirstOrDefault(x => x.Number == Number("slot")) ?? _detailSlot;
                    var targetInput = targetSlot?.InputBox?.Items.Cast<VmixInput>().FirstOrDefault(x => x.Key == Text("key"));
                    if (targetSlot?.InputBox == null) throw new InvalidOperationException("Slot video seçicisi hazır değil.");
                    if (targetInput == null) throw new InvalidOperationException("Seçilen video girişi bulunamadı. Girişleri yenileyip tekrar deneyin.");
                    targetSlot.InputBox.SelectedItem = targetInput;
                    break;
                case "sendSlotToOutput":
                    var outputSlot = _slots.FirstOrDefault(x => x.Number == Number("slot"))
                        ?? throw new InvalidOperationException("Çalışma alanı bulunamadı.");
                    if (string.IsNullOrWhiteSpace(outputSlot.InputKey)) throw new InvalidOperationException("Önce bu slota bir video kaynağı seçin.");
                    if (_outputMode == "vmix")
                    {
                        if (string.IsNullOrWhiteSpace(_apiBase)) throw new InvalidOperationException("Önce vMix’e bağlanın.");
                        if (outputSlot.Input == null || string.IsNullOrWhiteSpace(outputSlot.Input.Number)) throw new InvalidOperationException("Seçili vMix video girişi bulunamadı. Girişleri yenileyin.");
                        await ClearCurrentOutputTextAsync();
                        using var pauseResponse=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=Pause&Input={Uri.EscapeDataString(outputSlot.Input.Number)}",_lifetime.Token);pauseResponse.EnsureSuccessStatusCode();
                        using var rewindResponse=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=SetPosition&Input={Uri.EscapeDataString(outputSlot.Input.Number)}&Value=0",_lifetime.Token);rewindResponse.EnsureSuccessStatusCode();
                        var cutUrl = $"{_apiBase.TrimEnd('/')}/?Function=Cut&Input={Uri.EscapeDataString(outputSlot.Input.Number)}";
                        using var cutResponse = await _http.GetAsync(cutUrl, _lifetime.Token);
                        cutResponse.EnsureSuccessStatusCode();
                        using var playResponse=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=Play&Input={Uri.EscapeDataString(outputSlot.Input.Number)}",_lifetime.Token);playResponse.EnsureSuccessStatusCode();
                        GlobalStatus.Text = $"Slot {outputSlot.Number} videosu vMix PGM’e gönderildi.";
                    }
                    else if (_outputMode == "obs")
                    {
                        if (_obsClient?.IsConnected != true) throw new InvalidOperationException("Önce OBS’ye bağlanın.");
                        var activeScene = await _obsClient.GetCurrentProgramSceneAsync(_lifetime.Token);
                        var sceneItems = await _obsClient.GetSceneItemsAsync(activeScene,_lifetime.Token);
                        var selectedItem = sceneItems.FirstOrDefault(item => item.Name.Equals(outputSlot.InputKey,StringComparison.OrdinalIgnoreCase));
                        if (selectedItem == null) throw new InvalidOperationException($"'{outputSlot.InputKey}' kaynağı OBS’nin etkin program sahnesinde değil. Kaynağı OBS sahnesine ekleyip tekrar deneyin.");
                        _refreshing = true;
                        try
                        {
                            foreach (var slot in _slots.Where(slot => !string.IsNullOrWhiteSpace(slot.InputKey)))
                            {
                                foreach (var item in sceneItems.Where(item => item.Name.Equals(slot.InputKey,StringComparison.OrdinalIgnoreCase) && item.Id != selectedItem.Id && item.Enabled))
                                    await _obsClient.SetSceneItemEnabledAsync(activeScene,item.Id,false,_lifetime.Token);
                            }
                            await _obsClient.SetSceneItemEnabledAsync(activeScene,selectedItem.Id,true,_lifetime.Token);
                            await _obsClient.RestartMediaInputAsync(outputSlot.InputKey,_lifetime.Token);
                        }
                        finally { _refreshing = false; }
                        GlobalStatus.Text = $"Slot {outputSlot.Number} videosu OBS PGM sahnesinde etkinleştirildi.";
                    }
                    else throw new InvalidOperationException("Önce OBS veya vMix seçin.");
                    ResetPlaylistPlayback();
                    _broadcastCardId = $"slot-{outputSlot.Number}";
                    _broadcastCardInputKey = outputSlot.InputKey;
                    break;
                case "controlSetOrder": SetControlCardOrder(m.GetProperty("ids").Deserialize<string[]>()??Array.Empty<string>());break;
                case "controlMoveCard": MoveControlCard(Text("source"),Text("target"));break;
                case "controlAddVideo":
                    var singleInput=_inputs.FirstOrDefault(x=>x.Key==Text("key")&&x.Type.Equals("Video",StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("Bir video seçin.");
                    _playlists.Add(new VideoPlaylist(Guid.NewGuid().ToString("N"),singleInput.Title.Length>120?singleInput.Title[..120]:singleInput.Title,_outputMode,new(){new PlaylistVideo(singleInput.Key,singleInput.Display)}){SingleVideo=true});
                    break;
                case "controlPrepareSlot": await RunControlActionAsync("slot",Number("slot").ToString());break;
                case "controlPrepareList": await RunControlActionAsync("list",Text("listId"));break;
                case "controlSkipList": await SkipControlListAsync(Text("listId"),Number("direction"));break;
                case "controlPlay": await RunControlActionAsync("play",Text("cardId"));break;
                case "playlistCreate":
                    var createdPlaylist = new VideoPlaylist(Guid.NewGuid().ToString("N"), PlaylistName(Text("name")), _outputMode, new());
                    _playlists.Add(createdPlaylist); _selectedPlaylistId = createdPlaylist.Id;
                    break;
                case "playlistSelect":
                    _selectedPlaylistId = _playlists.FirstOrDefault(list => list.Id == Text("listId"))?.Id
                        ?? throw new InvalidOperationException("Oynatma listesi bulunamadı.");
                    break;
                case "playlistRename":
                    var renamedPlaylist = SelectedPlaylist();
                    _playlists[_playlists.IndexOf(renamedPlaylist)] = renamedPlaylist with { Name = PlaylistName(Text("name")) };
                    break;
                case "playlistDelete":
                    var deletedPlaylist = SelectedPlaylist();
                    if (_playingPlaylistId == deletedPlaylist.Id) ResetPlaylistPlayback();
                    _playlists.Remove(deletedPlaylist); _selectedPlaylistId = _playlists.FirstOrDefault(list => list.OutputMode == _outputMode)?.Id;
                    break;
                case "playlistAdd":
                    var addPlaylist = SelectedPlaylist();
                    var addedVideo = _inputs.FirstOrDefault(input => input.Key == Text("key") && input.Type.Equals("Video",StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("Eklemek için bir video seçin.");
                    if (_playingPlaylistId == addPlaylist.Id) ResetPlaylistPlayback();
                    addPlaylist.Videos.Add(new PlaylistVideo(addedVideo.Key, addedVideo.Display));
                    break;
                case "playlistRemove":
                case "playlistMove":
                    var editedPlaylist = SelectedPlaylist();
                    var editIndex = Number("index");
                    if (editIndex < 0 || editIndex >= editedPlaylist.Videos.Count) throw new InvalidOperationException("Video satırı bulunamadı.");
                    if (_playingPlaylistId == editedPlaylist.Id) ResetPlaylistPlayback();
                    if (command == "playlistRemove") editedPlaylist.Videos.RemoveAt(editIndex);
                    else
                    {
                        var destination = editIndex + Number("direction");
                        if (destination < 0 || destination >= editedPlaylist.Videos.Count) break;
                        var moved = editedPlaylist.Videos[editIndex]; editedPlaylist.Videos.RemoveAt(editIndex); editedPlaylist.Videos.Insert(destination,moved);
                    }
                    break;
                case "playlistStart":
                    var requestedPlaylistId = Text("listId");
                    var startPlaylist = string.IsNullOrWhiteSpace(requestedPlaylistId) ? SelectedPlaylist() : _playlists.FirstOrDefault(list => list.Id == requestedPlaylistId && list.OutputMode == _outputMode) ?? throw new InvalidOperationException("Oynatma listesi bulunamadı.");
                    _selectedPlaylistId = startPlaylist.Id;
                    if (startPlaylist.Videos.Count == 0) throw new InvalidOperationException("Listeye önce video ekleyin.");
                    var startIndex = 0;
                    await RefreshPlaylistDurationsAsync(startPlaylist);
                    _playlistQueue.Clear(); _playlistQueue.AddRange(startPlaylist.Videos.Select(video => video.Key));
                    _playingPlaylistId = startPlaylist.Id;
                    _playlistActive = true;
                    try { await PlayPlaylistItemAsync(startIndex); }
                    catch { ResetPlaylistPlayback(); throw; }
                    break;
                case "playlistNext":
                case "playlistPrevious":
                    if (_playingPlaylistId != _selectedPlaylistId || _playlistQueue.Count == 0) throw new InvalidOperationException("Önce seçili listeyi başlatın.");
                    var nextIndex = command == "playlistNext" ? _playlistIndex + 1 : _playlistIndex - 1;
                    if (nextIndex < 0 || nextIndex >= _playlistQueue.Count) break;
                    _playlistActive = true;
                    try { await PlayPlaylistItemAsync(nextIndex); }
                    catch { _playlistActive = false; throw; }
                    break;
                case "playlistStop":
                    _playlistActive = false;
                    GlobalStatus.Text = "Otomatik video sırası durduruldu. Şu anki video oynatılmaya devam ediyor.";
                    break;
                case "load": DetailBrowse_Click(this,new RoutedEventArgs()); break;
                case "clear": if (_detailSlot != null && DiscardDraft()) { await ClearSlotAsync(_detailSlot); LoadDetail(); } break;
                case "clearSlot":
                    var clearSlot = _slots.FirstOrDefault(x => x.Number == Number("slot"))
                        ?? throw new InvalidOperationException("Çalışma alanı bulunamadı.");
                    if (clearSlot == _detailSlot && !DiscardDraft()) break;
                    await ClearSlotAsync(clearSlot);
                    if (clearSlot == _detailSlot) LoadDetail();
                    break;
                case "clearAll": if (DiscardDraft()) { await ClearAllForWebAsync(); LoadDetail(); } break;
                case "add": AddSlot_Click(this,new RoutedEventArgs()); break;
                case "removeSlot": await RemoveSlotAsync(Number("slot")); break;
                case "translate": await TranslateSelectedAsync(); break;
                case "cue": var index = Number("index"); if (index < 0 || index >= _draft.Count) throw new InvalidOperationException("Altyazı satırı bulunamadı."); DetailCueBox.SelectedIndex = index; break;
                case "edit":
                    if (_detailSlot == null || _detailSlot.Number != Number("slot") || _draftVersion != Number("version") || DetailCueBox.SelectedIndex != Number("index") || !DetailTranslation.IsEnabled) throw new InvalidOperationException("Slot veya satır değişti; güncel metni yeniden açın.");
                    DetailTranslation.Text = Text("text"); break;
                case "apply": DetailApply_Click(this,new RoutedEventArgs()); break;
                case "save": DetailSave_Click(this,new RoutedEventArgs()); break;
                case "theme": SetWebTheme(Text("theme"),false); break;
                case "settings":
                    SettingsLanguageBox.Text = Text("language");
                    var theme = Text("theme"); ValidateWebTheme(theme);
                    var preferences = _preferences with { TargetLanguage = Text("language").Trim(), ColorTheme = theme, DarkMode = theme != "light", ThemeEnabled = false, InterfaceLanguage = ValidateInterfaceLanguage(Text("appLanguage")) };
                    preferences.Save(); _preferences = preferences; SetWebTheme(theme,false); UpdateChatGptStatus(); SettingsStatusText.Text = _preferences.InterfaceLanguage == "en" ? "Settings saved." : "Ayarlar kaydedildi."; break;
                case "appLanguage":
                    _preferences = _preferences with { InterfaceLanguage = ValidateInterfaceLanguage(Text("language")) };
                    _preferences.Save(); SettingsStatusText.Text = _preferences.InterfaceLanguage == "en" ? "Language saved." : "Uygulama dili kaydedildi."; break;
                case "profileSave":
                    SettingsLanguageBox.Text = Text("language");
                    ValidateWebTheme(Text("theme")); SetWebTheme(Text("theme"), false);
                    _preferences = _preferences with { InterfaceLanguage = ValidateInterfaceLanguage(Text("appLanguage")) };
                    _obsHost = Text("obsHost"); _obsPort = Number("obsPort");
                    _profileUi=m.TryGetProperty("ui",out var saveUi)?saveUi.Deserialize<ProfileUi>()??new():_profileUi;_profileUi.Validate();SaveProfile(false); break;
                case "profileSaveAs":
                    SettingsLanguageBox.Text = Text("language");
                    ValidateWebTheme(Text("theme")); SetWebTheme(Text("theme"), false);
                    _preferences = _preferences with { InterfaceLanguage = ValidateInterfaceLanguage(Text("appLanguage")) };
                    _obsHost = Text("obsHost"); _obsPort = Number("obsPort");
                    _profileUi=m.TryGetProperty("ui",out var saveAsUi)?saveAsUi.Deserialize<ProfileUi>()??new():_profileUi;_profileUi.Validate();SaveProfile(true); break;
                case "profileNew": await NewProfileAsync();break;
                case "profileLoad": await LoadProfileAsync(); break;
                case "profileOpenPath": await LoadProfileAsync(Text("path")); break;
                case "chatTranslate":
                    var chatText = Text("text");
                    var chatModel = (ChatGptModelBox.SelectedItem as ChatGptModel)?.Slug ?? "";
                commandResult = await _translator.TranslateTextAsync(chatText, chatModel, Text("language"), _lifetime.Token, _glossaryEntries.ToArray(),_preferences.TranslationPreset);
                    break;
                case "resetSettings": SetWebTheme(_preferences.ColorTheme,false); SettingsLanguageBox.Text = _preferences.TargetLanguage; SettingsStatusText.Text = "Kaydedilmiş ayarlara dönüldü."; break;
                default: throw new InvalidOperationException("Bilinmeyen arayüz işlemi.");
            }
            SendWebState(true); PostWeb(new { type="result",id,ok=true,value=commandResult });
        }
        catch(Exception ex) { if (!_closed) { GlobalStatus.Text = ex.Message; SendWebState(true); PostWeb(new { type="result",id,ok=false,error=ex.Message }); } }
    }
    private async Task ClearAllForWebAsync() { Clear_Click(this,new RoutedEventArgs()); await Task.CompletedTask; }
    private string InputTitle(string key,string fallback) => _outputMode == "vmix" && _vmixInputNames.TryGetValue(key,out var title) ? title : _inputs.FirstOrDefault(input => input.Key == key)?.Title ?? fallback;
    private static double? CardDuration(IReadOnlyDictionary<string,double> durations, string? key) =>
        key != null && durations.TryGetValue(key,out var value) && double.IsFinite(value) && value > 0 ? value : null;

    private static double? CardPlaylistDuration(IReadOnlyDictionary<string,double> durations, IReadOnlyList<PlaylistVideo> videos)
    {
        if (videos.Count == 0) return null;
        double total = 0;
        foreach (var video in videos)
        {
            if (CardDuration(durations,video.Key) is not double duration) return null;
            total += duration;
        }
        return double.IsFinite(total) ? total : null;
    }

    private void UpdateVmixMediaInformation(XDocument doc)
    {
        _vmixDurations.Clear(); _vmixInputNames.Clear();
        foreach (var input in doc.Descendants("input"))
        {
            var key = (string?)input.Attribute("key"); if (string.IsNullOrWhiteSpace(key)) continue;
            _vmixInputNames[key] = (string?)input.Attribute("title") ?? "";
            var duration = ParseVideoMilliseconds((string?)input.Attribute("duration") ?? (string?)input.Attribute("length"));
            if (duration is > 0) _vmixDurations[key] = duration.Value;
        }
    }
    private void StoreObsMediaDuration(string key,ObsMediaStatus media)
    {
        if (media.DurationMilliseconds is > 0) _obsDurations[key] = media.DurationMilliseconds.Value;
        else _obsDurations.Remove(key);
    }
    private async Task RefreshPlaylistDurationsAsync(VideoPlaylist playlist)
    {
        if (_refreshing) throw new InvalidOperationException("Video geçişi tamamlanana kadar bekleyin.");
        _refreshing = true;
        try
        {
            if (_outputMode == "vmix")
            {
                if (string.IsNullOrWhiteSpace(_apiBase)) throw new InvalidOperationException("Önce vMix’e bağlanın.");
                UpdateVmixMediaInformation(XDocument.Parse(await _http.GetStringAsync(_apiBase,_lifetime.Token)));
            }
            else
            {
                if (_obsClient?.IsConnected != true) throw new InvalidOperationException("Önce OBS’ye bağlanın.");
                foreach (var key in playlist.Videos.Select(video => video.Key).Distinct())
                    StoreObsMediaDuration(key,await _obsClient.GetMediaStatusAsync(key,_lifetime.Token));
            }
        }
        finally { _refreshing = false; }
    }
    private VideoPlaylist SelectedPlaylist() => _playlists.FirstOrDefault(list => list.Id == _selectedPlaylistId && list.OutputMode == _outputMode)
        ?? throw new InvalidOperationException("Bu yayın sistemi için bir oynatma listesi seçin.");
    private static string PlaylistName(string value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 120
        ? value.Trim() : throw new InvalidOperationException("Liste adı 1 ile 120 karakter arasında olmalı.");
    private void ResetPlaylistPlayback()
    { _preparedControlCard=null; _playlistActive = false; _playlistQueue.Clear(); _playlistIndex = -1; _playingPlaylistId = null; }
    private async Task PlayPlaylistItemAsync(int index,bool prepareOnly=false)
    {
        if(!prepareOnly)_preparedControlCard=null;
        if (index < 0 || index >= _playlistQueue.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var key = _playlistQueue[index];
        var input = _inputs.FirstOrDefault(item => item.Key.Equals(key,StringComparison.OrdinalIgnoreCase)
            && item.Type.Equals("Video",StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Listede bulunan video kaynağı artık mevcut değil. Kaynakları yenileyin.");
        if (_refreshing) throw new InvalidOperationException("Bağlantı yenilenirken video başlatılamaz.");
        _refreshing = true;
        try
        {
            await ClearCurrentOutputTextAsync();
            if (_outputMode == "vmix")
            {
                if (string.IsNullOrWhiteSpace(_apiBase) || string.IsNullOrWhiteSpace(input.Number)) throw new InvalidOperationException("Önce vMix’e bağlanıp video kaynaklarını yenileyin.");
                using var pause=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=Pause&Input={Uri.EscapeDataString(input.Number)}",_lifetime.Token);pause.EnsureSuccessStatusCode();
                using var rewind = await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=SetPosition&Input={Uri.EscapeDataString(input.Number)}&Value=0",_lifetime.Token);
                rewind.EnsureSuccessStatusCode();
                using var response = await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=Cut&Input={Uri.EscapeDataString(input.Number)}",_lifetime.Token);
                response.EnsureSuccessStatusCode();
                using var play = await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function={(prepareOnly?"Pause":"Play")}&Input={Uri.EscapeDataString(input.Number)}",_lifetime.Token);
                play.EnsureSuccessStatusCode();
                if(prepareOnly){using var reset=await _http.GetAsync($"{_apiBase.TrimEnd('/')}/?Function=SetPosition&Input={Uri.EscapeDataString(input.Number)}&Value=0",_lifetime.Token);reset.EnsureSuccessStatusCode();}
            }
            else if (_outputMode == "obs")
            {
                if (_obsClient?.IsConnected != true) throw new InvalidOperationException("Önce OBS’ye bağlanıp video kaynaklarını yenileyin.");
                var scene = await _obsClient.GetCurrentProgramSceneAsync(_lifetime.Token);
                var items = await _obsClient.GetSceneItemsAsync(scene,_lifetime.Token);
                var selected = items.FirstOrDefault(item => item.Name.Equals(input.Key,StringComparison.OrdinalIgnoreCase));
                if (selected == null) throw new InvalidOperationException($"'{input.Title}' kaynağı OBS’nin etkin program sahnesinde değil. Kaynağı OBS sahnesine ekleyip tekrar deneyin.");
                var mediaNames = _inputs.Where(item => item.Type.Equals("Video",StringComparison.OrdinalIgnoreCase)).Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items.Where(item => mediaNames.Contains(item.Name) && item.Id != selected.Id && item.Enabled))
                    await _obsClient.SetSceneItemEnabledAsync(scene,item.Id,false,_lifetime.Token);
                if (!selected.Enabled) await _obsClient.SetSceneItemEnabledAsync(scene,selected.Id,true,_lifetime.Token);
                await _obsClient.RestartMediaInputAsync(input.Key,_lifetime.Token);
                if(prepareOnly)await _obsClient.PauseAndRewindMediaAsync(input.Key,_lifetime.Token);
            }
            else throw new InvalidOperationException("Önce OBS veya vMix seçin.");
            _broadcastCardId = $"playlist-{_playingPlaylistId}";
            _broadcastCardInputKey = input.Key;
            _playlistIndex = index;
            _playlistTransitionUntil = DateTime.UtcNow.AddSeconds(2);
            _videoSource = input.Title;
            _videoPositionMilliseconds = 0;
            var durations = _outputMode == "vmix" ? _vmixDurations : _obsDurations;
            _videoDurationMilliseconds = durations.TryGetValue(input.Key,out var knownDuration) ? knownDuration : null;
            _videoIsPlaying = !prepareOnly;
            GlobalStatus.Text = $"Oynatma listesi • {index + 1}/{_playlistQueue.Count} • {input.Title}";
            InvalidateSubtitle();
        }
        finally { _refreshing = false; SendWebState(true); }
    }
    private async Task<bool> AdvancePlaylistIfFinishedAsync(string? currentKey,double? positionMilliseconds,double? durationMilliseconds,bool mediaEnded=false)
    {
        if (!_playlistActive || _playlistIndex < 0 || _playlistIndex >= _playlistQueue.Count) return false;
        var expectedKey = _playlistQueue[_playlistIndex];
        if (!string.Equals(expectedKey,currentKey,StringComparison.OrdinalIgnoreCase))
        {
            if (DateTime.UtcNow < _playlistTransitionUntil) return false;
            var currentIndex = currentKey == null ? -1 : _playlistQueue.FindIndex(key => key.Equals(currentKey,StringComparison.OrdinalIgnoreCase));
            if (currentIndex < 0) { _playlistActive = false; return false; }
            _playlistActive = false; return false;
        }
        if (DateTime.UtcNow < _playlistTransitionUntil) return false;
        var reachedEnd = mediaEnded || (positionMilliseconds is double position && durationMilliseconds is double duration && duration > 0 && position >= duration - 50);
        if (!reachedEnd) return false;
        if (_playlistIndex + 1 >= _playlistQueue.Count)
        {
            _playlistActive = false;
            GlobalStatus.Text = "Oynatma listesi tamamlandı.";
            SendWebState(true);
            return true;
        }
        try { await PlayPlaylistItemAsync(_playlistIndex + 1); }
        catch { _playlistActive = false; throw; }
        return true;
    }
    private string _webTheme = "graphite";
    private static void ValidateWebTheme(string value) { if (!AppPreferences.ThemeIds.Contains(value)) throw new ArgumentException("Geçersiz renk teması."); }
    private static string ValidateInterfaceLanguage(string value) => AppPreferences.InterfaceLanguageIds.Contains(value) ? value : throw new ArgumentException("Desteklenmeyen uygulama dili.");
    private void SetWebTheme(string theme,bool save)
    {
        ValidateWebTheme(theme); _webTheme = theme; UpdateWindowChrome();
        if (save) { var prefs = _preferences with { ColorTheme=theme,DarkMode=theme!="light" }; prefs.Save(); _preferences=prefs; }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    private void UpdateWindowChrome()
    {
        try { var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return; int dark = _webTheme == "light" ? 0 : 1; if (DwmSetWindowAttribute(handle,20,ref dark,sizeof(int)) != 0) DwmSetWindowAttribute(handle,19,ref dark,sizeof(int)); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
    private void SendWebState(bool force=false)
    {
        if (!_webReady || _closed) return;
        try
        {
            var connected = ConnectionText.Text.StartsWith("Connected",StringComparison.OrdinalIgnoreCase);
            var slot = _detailSlot;
            int i = DetailCueBox.SelectedIndex;
            var broadcastCardIsActive = _outputMode == "vmix" ? connected && _vmixProgramInputKey == _broadcastCardInputKey : _obsClient?.IsConnected == true && !string.IsNullOrEmpty(_videoSource) && _inputs.FirstOrDefault(input => input.Key == _broadcastCardInputKey)?.Title == _videoSource;
            var durations = _outputMode == "vmix" ? _vmixDurations : _obsDurations;
            var playlistTiming = PlaylistClock.Calculate(_playlistQueue,durations,_playlistIndex,_videoPositionMilliseconds);
            var playlistTimingActive = broadcastCardIsActive && _playingPlaylistId != null && _broadcastCardId == $"playlist-{_playingPlaylistId}";
            var state = new {
                type="state",updateBusy=_updateBusy,updateStatus=_updateStatus,availableUpdateVersion=_availableUpdate?.Version,profileUi=_profileUi,profileUiRevision=_profileUiRevision,translationPreset=_preferences.TranslationPreset,appVersion=AppVersion.Current,startup=_startupGate,startupOutputChosen=_startupOutputChosen,loginBusy=_signInCancellation!=null,languages=AppPreferences.Languages.Append(_preferences.TargetLanguage).Distinct().Select(x => new { key=x,label=x }).ToArray(),page=_page,theme=_webTheme,savedTheme=_preferences.ColorTheme,language=_preferences.TargetLanguage,
                status=GlobalStatus.Text,program=ProgramText.Text,api=ApiStatusText.Text,appLanguage=_preferences.InterfaceLanguage,
                vmix=new { hosts=_vmixFoundHosts,scanning=_vmixScanning,address=_apiBase ?? "",connected,text=ConnectionText.Text,programInputKey=_vmixProgramInputKey,previewInputKey=_vmixPreviewInputKey },
                outputMode=_outputMode,
                broadcastCard=broadcastCardIsActive ? _broadcastCardId : null,preparedCard=broadcastCardIsActive ? _preparedControlCard : null,controlCardOrder=_controlCardOrder.ToArray(),
                obs=new { connected=_obsClient?.IsConnected==true,status=_obsStatus,hosts=_obsFoundHosts.ToArray(),host=_obsHost,port=_obsPort },
                account=new { connected=_chatGptAccount.CanTranslate,text=ChatGptStatusText.Text,button=ChatGptConnectButton.Content?.ToString() },
                usage=new { translations=_sessionTranslations,cues=_sessionTranslatedCues,status=_usageStatus,lastActivity=_lastTranslationActivity?.ToString("dd MMM HH:mm",System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) ?? "—" },
                models=ChatGptModelBox.Items.Cast<ChatGptModel>().Select(x => new { key=x.Slug,label=x.DisplayName }).ToArray(),model=(ChatGptModelBox.SelectedItem as ChatGptModel)?.Slug ?? "",
                inputs=_inputs.Where(x => x.Type.Equals("Video",StringComparison.OrdinalIgnoreCase)).Select(x => new {key=x.Key,label=x.Display}).ToArray(),
                playlist=new { timing=new { active=playlistTimingActive,totalMs=playlistTiming.TotalMilliseconds,elapsedMs=playlistTiming.ElapsedMilliseconds,remainingMs=playlistTiming.RemainingMilliseconds,currentTitle=_videoSource },active=_playlistActive,index=_playlistIndex,selectedId=_selectedPlaylistId,playingId=_playingPlaylistId,lists=_playlists.Where(list => list.OutputMode == _outputMode).Select(list => new { id=list.Id,name=list.Name,singleVideo=list.SingleVideo,count=list.Videos.Count,totalMs=CardPlaylistDuration(durations,list.Videos),available=list.Videos.Count>0 && list.Videos.All(video => _inputs.Any(input => input.Key==video.Key)),videoLabels=list.Videos.Select(video => InputTitle(video.Key,video.Label)).ToArray() }).ToArray(),items=(_playlists.FirstOrDefault(list => list.Id == _selectedPlaylistId && list.OutputMode == _outputMode)?.Videos ?? new List<PlaylistVideo>()).Select((video,index) => new { key=video.Key,label=_inputs.FirstOrDefault(input => input.Key == video.Key)?.Display ?? video.Label,number=index+1,available=_inputs.Any(input => input.Key == video.Key) }).ToArray() },
                titles=TitleBox.Items.Cast<VmixInput>().Select(x => new {key=x.Key,label=x.Display}).ToArray(),title=(TitleBox.SelectedItem as VmixInput)?.Key ?? "",
                slots=_slots.Select(x => new { number=x.Number,language=x.UseSourceSubtitles?"__source__":x.RequestedLanguage ?? _preferences.TargetLanguage,sourceLanguage=x.SourceLanguageCode ?? "",input=x.InputKey ?? "",inputName=InputTitle(x.InputKey ?? "",x.Input?.Title ?? ""),inputNumber=x.Input?.Number ?? "",durationMs=CardDuration(durations,x.InputKey),file=Path.GetFileName(x.OutputPath ?? x.SrtPath ?? ""),status=x.Status?.Text,preview=x.Current?.Text,ready=x.Cues.Count>0,translating=x.TranslationCancellation!=null }).ToArray(),
                video=new { playing=_videoIsPlaying,positionMs=_videoPositionMilliseconds,durationMs=_videoDurationMilliseconds,remainingMs=_videoDurationMilliseconds is double duration && _videoPositionMilliseconds is double position ? Math.Max(0,duration-position) : (double?)null,source=_videoSource },
                omt=new { sources=_omtSources,source=_omtSource,connected=_omtReceiver != null },
                detail=new { number=slot?.Number ?? 0,file=DetailFile.Text,input=slot?.InputKey ?? "",count=_draft.Count,index=i,version=_draftVersion,original=DetailOriginal.Text,text=DetailTranslation.Text,editable=DetailTranslation.IsEnabled,dirty=_draftDirty,language=slot?.TargetLanguage ?? _preferences.TargetLanguage,status=DetailDraftStatus.Text,timing=i>=0 && i<_draft.Count ? SrtParser.Timecode(_draft[i].Start)+" → "+SrtParser.Timecode(_draft[i].End) : "" },
                settingsStatus=SettingsStatusText.Text,profiles=ListProfiles(),glossary=_glossaryEntries.Select(entry => new { source=entry.SourceTerm,target=entry.TargetTerm,language=entry.TargetLanguage,presetId=entry.Preset?.Id,presetLanguage=entry.Preset?.Language }).ToArray()
            };
            var json = JsonSerializer.Serialize(state);
            if (force || json != _lastWebState) { _lastWebState=json; WebSurface.CoreWebView2.PostWebMessageAsJson(json); }
        }
        catch(Exception ex) { GlobalStatus.Text="Arayüz güncellemesi: "+ex.Message; }
    }
    private void PostWeb(object value) { if (!_closed && WebSurface.CoreWebView2 != null) WebSurface.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value)); }
}
