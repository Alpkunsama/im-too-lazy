using Microsoft.Win32;
using System.IO;
using VmixSubtitleController.Output;

namespace VmixSubtitleController;

public partial class MainWindow
{
    private static string ActiveProfilePointerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "active-profile.txt");
    private static string ProfileCatalogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "profiles.txt");
    private string? _activeProfilePath;
    private ProfileUi _profileUi=new();
    private int _profileUiRevision;
    private string[]? _profilePathCache;
    private List<GlossaryEntry> _glossaryEntries = new();
    private string _obsHost = "127.0.0.1";
    private int _obsPort = 4455;
    private string? _connectedObsHost;
    private int _connectedObsPort;

    private static string? ReadActiveProfilePath()
    {
        try
        {
            var path = File.ReadAllText(ActiveProfilePointerPath).Trim();
            return File.Exists(path) ? path : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void RememberActiveProfilePath(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ActiveProfilePointerPath)!);
        File.WriteAllText(ActiveProfilePointerPath, path);
    }

    private void RegisterProfilePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var item in File.ReadAllLines(ProfileCatalogPath)) if (!string.IsNullOrWhiteSpace(item)) paths.Add(item.Trim()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        paths.Add(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ProfileCatalogPath)!);
        File.WriteAllLines(ProfileCatalogPath, paths.Where(File.Exists).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        _profilePathCache = null;
    }

    private object[] ListProfiles()
    {
        if (_profilePathCache == null)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var item in File.ReadAllLines(ProfileCatalogPath)) if (!string.IsNullOrWhiteSpace(item)) paths.Add(item.Trim()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "im too lazy", "Profiles");
            try { if (Directory.Exists(defaultFolder)) foreach (var item in Directory.EnumerateFiles(defaultFolder, "*.itlp.json")) paths.Add(item); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            _profilePathCache = paths.ToArray();
        }
        return _profilePathCache.Where(File.Exists).OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase).Select(path => new
        {
            path,
            name = Path.GetFileName(path).EndsWith(".itlp.json", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(path)[..^10] : Path.GetFileNameWithoutExtension(path),
            active = string.Equals(path, _activeProfilePath, StringComparison.OrdinalIgnoreCase)
        }).Cast<object>().ToArray();
    }

    private async Task NewProfileAsync()
    {
        await ClearCurrentOutputTextAsync();ResetPlaylistPlayback();StopOmtReceiver();
        foreach(var slot in _slots){slot.Editor?.Close();await ClearSlotAsync(slot);}
        while(_slots.Count>4)_slots.RemoveAt(_slots.Count-1);
        _preferences=new AppPreferences {ModelSlug=(ChatGptModelBox.SelectedItem as ChatGptModel)?.Slug};_preferences.Save();
        _activeProfilePath=null;_glossaryEntries.Clear();_playlists.Clear();_controlCardOrder.Clear();_selectedPlaylistId=null;_broadcastCardId=null;_broadcastCardInputKey=null;
        _vmixSlotInputKeys.Clear();_obsSlotInputKeys.Clear();_vmixTitleKey=null;_obsTitleKey=null;_selectedTitleKey=null;TitleBox.SelectedItem=null;
        _obsHost="127.0.0.1";_obsPort=4455;_profileUi=new();_profileUiRevision++;
        EnsureProfileSlotCount(4);foreach(var slot in _slots)slot.RequestedLanguage=_preferences.TargetLanguage;
        SettingsLanguageBox.Text=_preferences.TargetLanguage;SetWebTheme(_preferences.ColorTheme,false);_detailSlot=_slots[0];LoadDetail();ShowPage("home");
        SettingsStatusText.Text="Yeni profil hazır. Kaydet veya Farklı Kaydet ile adlandırabilirsiniz.";
    }
    private SubtitleProfile CaptureProfile()
    {
        SaveCurrentOutputMappings();
        return new SubtitleProfile
        {
            SlotCount = _slots.Count,
            Ui=_profileUi,SelectedPlaylistId=_selectedPlaylistId,SelectedSlot=_detailSlot?.Number??1,
            Subtitles = _slots.Select(slot=>new ProfileSubtitleSlot {Number=slot.Number,SrtPath=slot.SrtPath,OutputPath=slot.OutputPath,RequestedLanguage=slot.RequestedLanguage??_preferences.TargetLanguage,UseSourceSubtitles=slot.UseSourceSubtitles,TargetLanguage=slot.TargetLanguage,SourceLanguageCode=slot.SourceLanguageCode,HasManualEdits=slot.HasManualEdits,SourceCues=slot.SourceCues.ToList(),Cues=slot.Cues.ToList()}).ToList(),
            Preferences = _preferences with
            {
                TargetLanguage = SettingsLanguageBox.Text.Trim(),
                ColorTheme = _webTheme,
                DarkMode = _webTheme != "light"
            },
            OutputMode = _outputMode,
            VmixApi = _apiBase,
            ObsHost = _obsHost,
            ObsPort = _obsPort,
            VmixTitleKey = _vmixTitleKey,
            ObsTitleKey = _obsTitleKey,
            VmixSlotInputs = new Dictionary<int, string?>(_vmixSlotInputKeys),
            ObsSlotInputs = new Dictionary<int, string?>(_obsSlotInputKeys),
            Glossary = _glossaryEntries.ToList(),
            ControlCardOrder=_controlCardOrder.ToList(),
            ExplicitControlCards=true,
            Playlists = _playlists.ToList()
        };
    }

    private void ReplaceGlossary(List<GlossaryEntry> entries)
    {
        if (entries.Count > 5000) throw new InvalidOperationException("Sözlük en fazla 5000 terim içerebilir.");
        foreach (var entry in entries) entry.Validate();
        if (entries.GroupBy(entry => (entry.TargetLanguage, entry.SourceTerm), StringTupleComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Aynı hedef dil ve kaynak terim için tek bir karşılık ekleyin.");
        if (!string.IsNullOrWhiteSpace(_activeProfilePath) && File.Exists(_activeProfilePath))
        {
            var profile = SubtitleProfile.Load(_activeProfilePath) with { Glossary = entries };
            profile.Save(_activeProfilePath);
        }
        _glossaryEntries = entries;
    }

    private void EnsureProfileSlotCount(int count)
    {
        while (_slots.Count < count)
            _slots.Add(new Slot(_slots.Count + 1));
        BuildSlots();
    }

    private void SaveProfile(bool saveAs)
    {
        var path = _activeProfilePath;
        if (saveAs || string.IsNullOrWhiteSpace(path))
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "im too lazy", "Profiles");
            Directory.CreateDirectory(folder);
            var dialog = new SaveFileDialog
            {
                Title = "Profili farklı kaydet",
                Filter = "im too lazy profili (*.itlp.json)|*.itlp.json|JSON dosyası (*.json)|*.json",
                DefaultExt = ".itlp.json",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = "Yeni Profil.itlp.json"
            };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }

        CaptureProfile().Save(path!);
        _preferences = _preferences with { TargetLanguage = SettingsLanguageBox.Text.Trim(), ColorTheme = _webTheme, DarkMode = _webTheme != "light" };
        _preferences.Save();
        _activeProfilePath = path;
        RememberActiveProfilePath(path!);
        RegisterProfilePath(path!);
        SettingsStatusText.Text = _preferences.InterfaceLanguage == "en"
            ? $"Profile saved: {Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path!))}"
            : $"Profil kaydedildi: {Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path!))}";
        GlobalStatus.Text = SettingsStatusText.Text;
    }

    private async Task LoadProfileAsync(string? selectedPath = null)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "im too lazy", "Profiles");
        Directory.CreateDirectory(folder);
        var profilePath = selectedPath;
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            var dialog = new OpenFileDialog
            {
                Title = "Profil aç",
                Filter = "im too lazy profili (*.itlp.json)|*.itlp.json|JSON dosyası (*.json)|*.json",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = folder
            };
            if (dialog.ShowDialog(this) != true) return;
            profilePath = dialog.FileName;
        }
        profilePath = Path.GetFullPath(profilePath);
        if (!File.Exists(profilePath)) throw new FileNotFoundException("Profil dosyası bulunamadı.", profilePath);

        var profile = SubtitleProfile.Load(profilePath);
        SaveCurrentOutputMappings();
        foreach(var slot in _slots){slot.Editor?.Close();await ClearSlotAsync(slot);}
        while(_slots.Count>profile.SlotCount)_slots.RemoveAt(_slots.Count-1);
        EnsureProfileSlotCount(profile.SlotCount);
        _preferences = profile.Preferences;
        _glossaryEntries = profile.Glossary.ToList();
        _playlists = profile.Playlists.ToList();
        RestoreControlCards(profile);
        _selectedPlaylistId = _playlists.FirstOrDefault()?.Id;
        ResetPlaylistPlayback();
        _preferences.Save();
        _webTheme = _preferences.ColorTheme;
        _vmixTitleKey = profile.VmixTitleKey;
        _obsTitleKey = profile.ObsTitleKey;
        _vmixSlotInputKeys.Clear();
        foreach (var item in profile.VmixSlotInputs) _vmixSlotInputKeys[item.Key] = item.Value;
        _obsSlotInputKeys.Clear();
        foreach (var item in profile.ObsSlotInputs) _obsSlotInputKeys[item.Key] = item.Value;
        foreach(var slot in _slots){var mapping=_outputMode=="obs"?_obsSlotInputKeys:_vmixSlotInputKeys;slot.InputKey=mapping.GetValueOrDefault(slot.Number);}
        _obsHost = profile.ObsHost;
        _obsPort = profile.ObsPort;
        _activeProfilePath = profilePath;
        RememberActiveProfilePath(profilePath);
        RegisterProfilePath(profilePath);
        SettingsLanguageBox.Text = _preferences.TargetLanguage;
        SettingsDarkBox.IsChecked = _preferences.DarkMode;
        ChatGptModelBox.SelectedItem = ChatGptModelBox.Items.Cast<ChatGptModel>().FirstOrDefault(x => x.Slug == _preferences.ModelSlug);
        SetWebTheme(_preferences.ColorTheme, false);

        var restoredConnection = false;
        if (profile.OutputMode == "vmix" && !string.IsNullOrWhiteSpace(_apiBase) &&
            string.Equals(_apiBase, profile.VmixApi, StringComparison.OrdinalIgnoreCase))
        {
            _outputMode = "vmix";
            _subtitleOutput = _vmixSubtitleOutput;
            _vmixTitleKey = profile.VmixTitleKey;
            await RefreshInputsAsync();
            restoredConnection = true;
        }
        else if (profile.OutputMode == "obs" && _obsClient?.IsConnected == true &&
                 string.Equals(_connectedObsHost, profile.ObsHost, StringComparison.OrdinalIgnoreCase) && _connectedObsPort == profile.ObsPort)
        {
            _outputMode = "obs";
            _subtitleOutput = new ObsSubtitleOutput(_obsClient, () => _selectedTitleKey);
            await ApplyObsInputsAsync(await _obsClient.GetInputsAsync(_lifetime.Token));
            restoredConnection = true;
        }

        foreach(var saved in profile.Subtitles)
        {
            var slot=_slots.First(x=>x.Number==saved.Number);
            slot.SrtPath=saved.SrtPath;slot.OutputPath=saved.OutputPath;slot.RequestedLanguage=saved.RequestedLanguage??_preferences.TargetLanguage;slot.UseSourceSubtitles=saved.UseSourceSubtitles;slot.TargetLanguage=saved.TargetLanguage;slot.SourceLanguageCode=saved.SourceLanguageCode;slot.HasManualEdits=saved.HasManualEdits;
            slot.SourceCues=saved.SourceCues.ToList();slot.Cues=saved.Cues.ToList();slot.TranslationVersion++;
            slot.TranslationState=slot.Cues.Count>0?$"{slot.TargetLanguage} hazır • {slot.Cues.Count} altyazı":slot.SourceCues.Count>0?"SRT hazır • Çevir ile devam edin":"";
            slot.SrtBox!.Text=Path.GetFileName(slot.OutputPath??slot.SrtPath??"");slot.SrtBox.ToolTip=slot.OutputPath??slot.SrtPath;slot.PreviewButton!.IsEnabled=slot.Cues.Count>0;slot.RetryButton!.IsEnabled=slot.SourceCues.Count>0;UpdateSlotStatus(slot);
        }
        _profileUi=profile.Ui;_profileUiRevision++;
        _detailSlot=_slots.FirstOrDefault(slot=>slot.Number==profile.SelectedSlot)??_slots[0];LoadDetail();
        SettingsStatusText.Text = _preferences.InterfaceLanguage == "en"
            ? $"Profile opened: {Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(profilePath))}" + (restoredConnection ? " • matching connection restored" : " • reconnect from the Settings page")
            : $"Profil açıldı: {Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(profilePath))}" + (restoredConnection ? " • eşleşen bağlantı ayarlandı" : " • bağlanmak için Ayarlar sayfasını kullanın");
        _selectedPlaylistId = _playlists.FirstOrDefault(list => list.Id==profile.SelectedPlaylistId&&list.OutputMode==_outputMode)?.Id??_playlists.FirstOrDefault(list => list.OutputMode == _outputMode)?.Id;
        GlobalStatus.Text = SettingsStatusText.Text;
        InvalidateSubtitle();
        SendWebState(true);
    }
}
