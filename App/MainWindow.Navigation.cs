using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace VmixSubtitleController;
public partial class MainWindow
{
    private Slot? _detailSlot;
    private List<SubtitleCue> _draft = new();
    private int _draftVersion;
    private List<SubtitleCue>? _draftBasis;
    private bool _detailUpdating, _draftDirty;
    private string _page = "home";
    private readonly Dictionary<Slot, Grid> _slotRows = new();
    private readonly DispatcherTimer _dashboardTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private void InitializeDashboard()
    {
        DarkModeBox.IsChecked = _preferences.DarkMode;
        SelectDetail(_slots[0]); ShowPage("home");
        _dashboardTimer.Tick += (_, _) => RefreshDashboard();
        _dashboardTimer.Start(); Closed += (_, _) => _dashboardTimer.Stop();
    }
    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage("home");
    private void SlotsNav_Click(object sender, RoutedEventArgs e) => ShowPage("slots");
    private void ConnectionsNav_Click(object sender, RoutedEventArgs e) => ShowPage("connections");
    private void EditorNav_Click(object sender, RoutedEventArgs e) => DetailPreview_Click(sender,e);
    private void ShowPage(string page)
    {
        _page = page;
        SlotsPage.Visibility = page is "broadcast" or "slots" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsPage.Visibility = page is "broadcast" or "connections" ? Visibility.Visible : Visibility.Collapsed;
        ActionCards.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        PageHeading.Text = page == "home" ? "Ana Sayfa" : page == "translate" ? "Çeviri" : page == "broadcast" ? "Broadcast" : page == "profiles" ? "Profiller" : page == "editor" ? "SRT Editörü" : page == "slots" ? "Çalışma Alanı" : page == "settings" ? "Ayarlar" : "Bağlantılar";
        PageDescription.Text = page == "settings" ? "Çeviri dilini ve pencere görünümünü burada düzenleyin." : page is "connections" or "broadcast" ? "vMix çıkışını, ChatGPT hesabını ve çeviri modelini yönetin." : "Canlı yayınlarınız için hızlı ve güvenilir altyazı yönetimi";
        HomeNav.Background = page == "home" ? Brush("#303338") : Brushes.Transparent;
        SlotsNav.Background = page == "broadcast" ? Brush("#303338") : Brushes.Transparent;
        ConnectionsNav.Background = page == "broadcast" ? Brush("#303338") : Brushes.Transparent;
        SettingsNav.Background = page == "settings" ? Brush("#303338") : Brushes.Transparent;
    }
    private SolidColorBrush Brush(string color)
    {
        var value = (Color)ColorConverter.ConvertFromString(color);
        return new SolidColorBrush(DarkModeBox?.IsChecked == false ? LightColor(value) : value);
    }
    private static Color LightColor(Color c)
    {
        if (Math.Max(c.R,Math.Max(c.G,c.B)) - Math.Min(c.R,Math.Min(c.G,c.B)) > 45) return c;
        int brightness = (c.R+c.G+c.B)/3;
        byte level = brightness < 110 ? (byte)Math.Clamp(265-brightness,150,248) : (byte)Math.Clamp(255-brightness,25,100);
        return Color.FromArgb(c.A,level,level,level);
    }
    private void BuildSlotRow(Slot slot)
    {
        var row = new Grid { MinHeight = 62, Background = Brush("#202225"), Margin = new Thickness(0,0,0,1) };
        foreach (var width in new[]{88,154,190,140,-1,82}) row.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1,GridUnitType.Star) : new GridLength(width) });
        void Add(UIElement element,int column) { Grid.SetColumn(element,column); row.Children.Add(element); }
        var select = new Button { Content = $"Slot {slot.Number}", Margin = new Thickness(8,10,12,10), Padding = new Thickness(5) };
        select.Click += (_,_) => SelectDetail(slot); Add(select,0);
        var input = new ComboBox { DisplayMemberPath = "Display", Margin = new Thickness(0,10,14,10) };
        slot.InputBox = input; input.ItemsSource = _inputs.Where(i => i.Type.Equals("Video",StringComparison.OrdinalIgnoreCase)).ToList();
        input.SelectedItem = input.Items.Cast<VmixInput>().FirstOrDefault(i => i.Key == slot.InputKey);
        input.SelectionChanged += (_,_) => { if (_updatingInputLists) return; slot.Input = input.SelectedItem as VmixInput; slot.InputKey = slot.Input?.Key; if (_outputMode == "obs") _obsSlotInputKeys[slot.Number] = slot.InputKey; else _vmixSlotInputKeys[slot.Number] = slot.InputKey; InvalidateSubtitle(); UpdateSlotStatus(slot); if (_detailSlot == slot) RefreshDetailInput(); };
        Add(input,1);
        var file = new TextBox { IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "SRT seçmek için çift tıklayın", Text = Path.GetFileName(slot.OutputPath ?? slot.SrtPath ?? "") };
        file.MouseDoubleClick += async (_,_) => { if (SelectDetail(slot)) await BrowseSrtAsync(slot); }; slot.SrtBox = file; Add(file,2);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6) }; slot.Status = status; Add(status,3);
        var current = new TextBlock { Text = "—", TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8), MaxHeight = 48, Foreground = Brush("#A2A6AC") }; slot.Current = current; Add(current,4);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var preview = new Button { Content = "▶", Width = 34, Height = 34, Padding = new Thickness(4), IsEnabled = slot.Cues.Count > 0, ToolTip = "Altyazı önizleme" }; slot.PreviewButton = preview;
        preview.Click += (_,_) => { if (SelectDetail(slot)) OpenPreview(slot); }; buttons.Children.Add(preview);
        var more = new Button { Content = "⋯", Width = 34, Height = 34, Padding = new Thickness(4), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(5,0,0,0) };
        var menu = new ContextMenu();
        var browse = new MenuItem { Header = "SRT yükle" }; browse.Click += async (_,_) => { if (SelectDetail(slot)) await BrowseSrtAsync(slot); }; menu.Items.Add(browse);
        // Keep the existing translation button state contract even though actions now live in a menu.
        slot.RetryButton = new Button { Content = "Çevir", IsEnabled = slot.SourceCues.Count > 0 };
        var translate = new MenuItem { Header = "Çevir / İptal" }; translate.Click += async (_,_) => { if (SelectDetail(slot)) await TranslateSelectedAsync(); }; menu.Items.Add(translate);
        var clear = new MenuItem { Header = "Slotu temizle" }; clear.Click += async (_,_) => { if (!SelectDetail(slot) || !DiscardDraft()) return; await ClearSlotAsync(slot); LoadDetail(); }; menu.Items.Add(clear);
        more.ContextMenu = menu; more.Click += (_,_) => { menu.PlacementTarget = more; menu.IsOpen = true; }; buttons.Children.Add(more); Add(buttons,5);
        _slotRows[slot] = row; SlotsPanel.Children.Add(row); UpdateSlotStatus(slot);
    }
    private bool DiscardDraft() => !_draftDirty || StudioDialog.Show(this,"Uygulanmamış düzenlemeler var. Bu düzenlemelerden vazgeçilsin mi?","Altyazı düzenlemesi",MessageBoxButton.YesNo,MessageBoxImage.Question) == MessageBoxResult.Yes;
    private bool SelectDetail(Slot slot)
    {
        if (_detailSlot == slot) return true;
        if (!DiscardDraft()) return false;
        _detailSlot = slot; LoadDetail(); return true;
    }
    private void LoadDetail()
    {
        if (_detailSlot == null) return;
        _detailUpdating = true;
        try {
            var s = _detailSlot; _draftVersion = s.TranslationVersion; _draftBasis = s.Cues; _draft = s.Cues.ToList(); _draftDirty = false;
            DetailSlotLabel.Text = $"Slot {s.Number}"; DetailFile.Text = Path.GetFileName(s.OutputPath ?? s.SrtPath ?? ""); DetailFile.ToolTip = s.OutputPath ?? s.SrtPath;
            DetailCueBox.ItemsSource = _draft.Select((c,i) => $"{i+1} · {SrtParser.Timecode(c.Start)}").ToList(); DetailCueBox.SelectedIndex = _draft.Count > 0 ? 0 : -1;
            DetailTranslationLabel.Text = "Çevrilen Metin (" + s.TargetLanguage + ")"; DetailDraftStatus.Text = _draft.Count == 0 ? "SRT yükleyin ve çeviriyi tamamlayın." : "Düzenlemeler Slota Uygula ile yayına aktarılır.";
        } finally { _detailUpdating = false; }
        RefreshDetailInput(); LoadCue();
    }
    private void RefreshDetailInput()
    {
        _detailUpdating = true;
        try { DetailInputBox.ItemsSource = _detailSlot?.InputBox?.ItemsSource; DetailInputBox.SelectedItem = _detailSlot?.InputBox?.SelectedItem; }
        finally { _detailUpdating = false; }
    }
    private void LoadCue()
    {
        _detailUpdating = true;
        try { int i = DetailCueBox.SelectedIndex; DetailOriginal.Text = i >= 0 && i < (_detailSlot?.SourceCues.Count ?? 0) ? _detailSlot!.SourceCues[i].Text : ""; DetailTranslation.Text = i >= 0 && i < _draft.Count ? _draft[i].Text : ""; DetailTranslation.IsEnabled = i >= 0 && _detailSlot?.TranslationCancellation == null; }
        finally { _detailUpdating = false; }
    }
    private void DetailInput_Changed(object sender, SelectionChangedEventArgs e) { if (!_detailUpdating && _detailSlot?.InputBox != null) _detailSlot.InputBox.SelectedItem = DetailInputBox.SelectedItem; }
    private void DetailCue_Changed(object sender, SelectionChangedEventArgs e) { if (!_detailUpdating) LoadCue(); }
    private void DetailTranslation_Changed(object sender, TextChangedEventArgs e)
    {
        if (_detailUpdating || DetailCueBox == null) return;
        int i = DetailCueBox.SelectedIndex; if (i < 0 || i >= _draft.Count) return;
        _draft[i] = _draft[i] with { Text = DetailTranslation.Text }; _draftDirty = true; DetailDraftStatus.Text = "Düzenleme henüz slota uygulanmadı.";
    }
    private async void DetailBrowse_Click(object sender, RoutedEventArgs e) { if (_detailSlot != null && DiscardDraft()) { await BrowseSrtAsync(_detailSlot); LoadDetail(); } }
    private void DetailPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_detailSlot == null) return;
        if (_detailSlot.Cues.Count == 0) { DetailDraftStatus.Text = "Önizleme için önce sağdaki … düğmesinden SRT yükleyin ve çeviriyi tamamlayın."; GlobalStatus.Text = "Önizleme: altyazı henüz hazır değil."; return; }
        if (_draftDirty) { DetailDraftStatus.Text = "Sağdaki taslak henüz slota uygulanmadı. Önizleme mevcut slot metnini gösterir."; }
        OpenPreview(_detailSlot);
    }
    private async void TranslateSelected_Click(object sender, RoutedEventArgs e) => await TranslateSelectedAsync();
    private async Task TranslateSelectedAsync()
    {
        var s = _detailSlot; if (s == null) return;
        if (s.TranslationCancellation != null) { s.TranslationCancellation.Cancel(); return; }
        if (s.SourceCues.Count == 0) { DetailDraftStatus.Text = "Önce sağdaki … düğmesinden SRT yükleyin."; GlobalStatus.Text = "Çeviri: SRT dosyası bekleniyor."; return; }
        if (!DiscardDraft()) return;
        if (s.HasManualEdits && StudioDialog.Show(this,"Yeniden çeviri uygulanan düzeltmelerin yerini alacak. Devam edilsin mi?","Yeniden çevir",MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var version = s.TranslationVersion;
        if(s.UseSourceSubtitles){UseOriginalSubtitles(s);LoadDetail();return;}
        if (await EnsureChatGptConnectedAsync() && !_closed && version == s.TranslationVersion) await TranslateSlotAsync(s);
        if (_detailSlot == s) LoadDetail();
    }
    private void DetailApply_Click(object sender, RoutedEventArgs e)
    {
        var s = _detailSlot; if (s == null || _draft.Count == 0) { DetailDraftStatus.Text = "Uygulanacak altyazı yok. Önce SRT yükleyin."; return; }
        try {
            if (s.TranslationCancellation != null || s.TranslationVersion != _draftVersion || (!ReferenceEquals(s.Cues,_draftBasis) || s.Cues.Count != _draft.Count)) throw new InvalidOperationException("Slot değişti. Güncel altyazıyı açın; taslağınızı SRT olarak kaydedebilirsiniz.");
            SrtParser.Serialize(_draft); s.Cues = _draft.ToList(); _draftBasis = s.Cues; s.HasManualEdits = true; _draftDirty = false;
            s.TranslationState = $"Düzeltilmiş {s.TargetLanguage} • {s.Cues.Count} altyazı"; InvalidateSubtitle(); UpdateSlotStatus(s); DetailDraftStatus.Text = "Düzenlemeler slota uygulandı.";
        } catch(Exception ex) { StudioDialog.Show(this,ex.Message,"Altyazı hatası"); }
    }
    private void DetailSave_Click(object sender, RoutedEventArgs e)
    {
        if (_draft.Count == 0) { DetailDraftStatus.Text = "Kaydedilecek altyazı yok. Önce SRT yükleyin."; return; }
        try { var text = SrtParser.Serialize(_draft); var dialog = new SaveFileDialog { Filter = "SRT (*.srt)|*.srt", FileName = Path.GetFileName(_detailSlot?.OutputPath ?? "translated.srt"), DefaultExt = ".srt", AddExtension = true }; if (dialog.ShowDialog(this) != true) return; File.WriteAllText(dialog.FileName,text,new UTF8Encoding(true)); DetailDraftStatus.Text = "SRT kaydedildi. Yayına aktarmak için Slota Uygula'yı kullanın."; }
        catch(Exception ex) { StudioDialog.Show(this,ex.Message,"SRT kaydetme hatası"); }
    }
    private void AddSlot_Click(object sender, RoutedEventArgs e)
    {
        if (_slots.Count >= 100) { GlobalStatus.Text = "En fazla 100 çalışma alanı eklenebilir."; return; }
        var slot = new Slot(_slots.Count+1) {RequestedLanguage=_preferences.TargetLanguage}; _slots.Add(slot); BuildSlotRow(slot); SelectDetail(slot); FilterRows();
    }
    private async Task RemoveSlotAsync(int number)
    {
        if (_slots.Count <= 1) throw new InvalidOperationException("En az bir çalışma alanı kalmalı.");
        var slot = _slots.FirstOrDefault(item => item.Number == number)
            ?? throw new InvalidOperationException("Çalışma alanı bulunamadı.");
        if (_detailSlot == slot && !DiscardDraft()) return;
        var answer = StudioDialog.Show(this,
            $"Slot {number} ve bu slota ait geçerli oturum verileri kaldırılacak. Devam edilsin mi?",
            "Slotu kaldır", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        SaveCurrentOutputMappings();
        var oldIndex = _slots.IndexOf(slot);
        slot.Editor?.Close();
        CancelTranslation(slot);
        _slots.Remove(slot);
        _slotRows.Remove(slot);

        static void RemoveAndShift(Dictionary<int, string?> mappings, int removedNumber)
        {
            var kept = mappings.Where(pair => pair.Key != removedNumber).ToArray();
            mappings.Clear();
            foreach (var pair in kept)
                mappings[pair.Key > removedNumber ? pair.Key - 1 : pair.Key] = pair.Value;
        }
        RemoveAndShift(_vmixSlotInputKeys, number);
        RemoveAndShift(_obsSlotInputKeys, number);
        ShiftControlSlotsAfterRemoval(number);
        foreach (var remaining in _slots.Where(item => item.Number > number)) remaining.Number--;

        if (_detailSlot == slot) { _detailSlot = null; _draftDirty = false; }
        BuildSlots();
        if (_detailSlot == null) SelectDetail(_slots[Math.Min(oldIndex, _slots.Count - 1)]);
        else DetailSlotLabel.Text = $"Slot {_detailSlot.Number}";
        FilterRows();
        InvalidateSubtitle();
        GlobalStatus.Text = $"Slot {number} kaldırıldı.";
        SendWebState(true);
        await Task.CompletedTask;
    }
    private void DashboardSearch_Changed(object sender, TextChangedEventArgs e) { if (SlotsPanel != null) FilterRows(); }
    private void FilterRows()
    {
        var q = DashboardSearch.Text.Trim();
        foreach(var pair in _slotRows) { var s = pair.Key; var text = $"Slot {s.Number} {s.Input?.Display} {s.SrtPath} {s.OutputPath} {s.Current?.Text}"; pair.Value.Visibility = q.Length == 0 || text.Contains(q,StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; }
    }
    private void RefreshDashboard()
    {
        VmixBadge.Text = "●  " + ConnectionText.Text; VmixBadge.Foreground = ConnectionDot.Fill;
        AccountBadge.Text = _chatGptAccount.CanTranslate ? "●  ChatGPT Bağlı" : "●  ChatGPT bağlı değil"; AccountBadge.Foreground = _chatGptAccount.CanTranslate ? Brush("#82BD91") : Brush("#A2A6AC");
        AccountSummary.Text = _chatGptAccount.CanTranslate ? "Bağlı\n" + ((ChatGptModelBox.SelectedItem as ChatGptModel)?.DisplayName ?? "Model seçin") : "Bağlı değil";
        EngineSummary.Text = _slots.Any(s => s.TranslationCancellation != null) ? "Çeviriliyor" : _chatGptAccount.CanTranslate && ChatGptModelBox.SelectedItem != null ? "Hazır\nOtomatik çeviri aktif" : "Hesap / model bekleniyor";
        ClockText.Text = DateTime.Now.ToString("dd MMM yyyy  HH:mm");
        foreach(var pair in _slotRows) pair.Value.Background = RowSurface(pair.Key == _detailSlot);
        if (_detailSlot != null && !_draftDirty && (_draftVersion != _detailSlot.TranslationVersion || !ReferenceEquals(_draftBasis,_detailSlot.Cues) || _draft.Count != _detailSlot.Cues.Count)) LoadDetail();
        if (_detailSlot != null) DetailTranslation.IsEnabled = DetailCueBox.SelectedIndex >= 0 && _detailSlot.TranslationCancellation == null && _draftVersion == _detailSlot.TranslationVersion && ReferenceEquals(_draftBasis,_detailSlot.Cues);
        if (_detailSlot != null && DetailInputBox.ItemsSource != _detailSlot.InputBox?.ItemsSource) RefreshDetailInput();
        if (_draftDirty && _detailSlot != null && (_draftVersion != _detailSlot.TranslationVersion || !ReferenceEquals(_draftBasis,_detailSlot.Cues))) DetailDraftStatus.Text = "Slot değişti. Bu taslak yalnızca SRT olarak kaydedilebilir.";
        FilterRows();
    }
    private void ThemeMode_Changed(object sender, RoutedEventArgs e)
    {
        if (DarkModeBox == null) return;
        foreach (var key in Resources.Keys.Cast<object>().ToList())
        {
            if (Resources[key] is not SolidColorBrush brush) continue;
            if (!_darkPalette.ContainsKey(key)) _darkPalette[key] = brush.Color;
            Resources[key] = new SolidColorBrush(DarkModeBox.IsChecked == true ? _darkPalette[key] : LightColor(_darkPalette[key]));
        }
        foreach (var key in new[]{"GlassWindowBrush","GlassPanelBrush","GlassSidebarBrush","GlassLineBrush"})
        {
            if (!_glassPalette.ContainsKey(key)) _glassPalette[key] = ((LinearGradientBrush)Resources[key]).Clone();
            var gradient = _glassPalette[key].Clone();
            if (DarkModeBox.IsChecked != true) foreach (var stop in gradient.GradientStops) stop.Color = LightColor(stop.Color);
            Resources[key] = gradient;
        }
        SetResourceReference(BackgroundProperty,"GlassWindowBrush");
        if (SettingsDarkBox != null) SettingsDarkBox.IsChecked = DarkModeBox.IsChecked;
    }
    private readonly Dictionary<object,Color> _darkPalette = new();
    private readonly Dictionary<string,LinearGradientBrush> _glassPalette = new();
    private Brush RowSurface(bool selected)
    {
        var gradient = ((LinearGradientBrush)Resources["GlassPanelBrush"]).Clone();
        gradient.Opacity = selected ? 1.0 : 0.58;
        return gradient;
    }
    private void SettingsTheme_Changed(object sender, RoutedEventArgs e)
    {
        if (SettingsDarkBox != null && DarkModeBox != null && SettingsDarkBox.IsChecked != DarkModeBox.IsChecked) DarkModeBox.IsChecked = SettingsDarkBox.IsChecked;
    }
    private void SaveInlineSettings_Click(object sender, RoutedEventArgs e)
    {
        try {
            var value = _preferences with { TargetLanguage = SettingsLanguageBox.Text.Trim(), DarkMode = DarkModeBox.IsChecked == true, ThemeEnabled = false };
            value.Save(); _preferences = value; UpdateChatGptStatus(); SettingsStatusText.Text = "Ayarlar kaydedildi. Hedef dil yeni çevirilerde kullanılacak.";
        } catch (Exception ex) { SettingsStatusText.Text = "Ayarlar kaydedilemedi: " + ex.Message; }
    }
    private void ResetInlineSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsLanguageBox.Text = _preferences.TargetLanguage; DarkModeBox.IsChecked = _preferences.DarkMode; SettingsDarkBox.IsChecked = _preferences.DarkMode; SettingsStatusText.Text = "Kaydedilmiş ayarlara dönüldü.";
    }
}
