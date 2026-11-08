using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VmixSubtitleController;

public partial class SubtitlePreviewWindow : Window
{
    private readonly ObservableCollection<EditableCue> _rows;
    private readonly Func<List<SubtitleCue>, bool> _apply;
    private readonly string _suggestedName;
    private readonly string? _initialDirectory;
    private bool _dirty;

    public SubtitlePreviewWindow(int slot, string path, IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated, string language, Func<List<SubtitleCue>, bool> apply)
    {
        InitializeComponent();
        _apply = apply;
        _initialDirectory = Path.GetDirectoryName(path);
        _suggestedName = Path.GetFileNameWithoutExtension(path) + "." + SrtParser.LanguageTag(language) + ".srt";
        Heading.Text = $"Slot {slot:00} / {language} / {Path.GetFileName(path)}";
        TranslationColumn.Header = language + " — düzenlenebilir";
        _rows = new ObservableCollection<EditableCue>(translated.Select((cue, index) =>
            new EditableCue(index + 1, cue, index < source.Count ? source[index].Text : "")));
        foreach (var row in _rows) row.PropertyChanged += (_, _) =>
        {
            _dirty = true;
            EditorStatus.Text = "Düzeltmeler henüz slota uygulanmadı veya kaydedilmedi.";
            UpdateSelected();
        };
        CueGrid.ItemsSource = _rows;
        if (_rows.Count > 0) CueGrid.SelectedIndex = 0;
        EditorStatus.Text = $"{_rows.Count} altyazı. Düzeltmeler yalnızca Slota uygula ile yayına aktarılır.";
        Closing += (_, e) =>
        {
            if (_dirty && StudioDialog.Show(this, "Uygulanmamış veya kaydedilmemiş düzeltmeleri kapatmak istiyor musunuz?", "Düzeltmeler", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                e.Cancel = true;
        };
    }

    public void MarkStale()
    {
        ApplyButton.IsEnabled = false;
        EditorStatus.Text = "Slot değişti. Bu önizleme artık slota uygulanamaz; düzeltmeleri SRT olarak kaydedebilirsiniz.";
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_rows == null) return;
        var query = SearchBox.Text.Trim();
        CollectionViewSource.GetDefaultView(_rows).Filter = item => item is EditableCue row &&
            (query.Length == 0 || row.Original.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
             row.Translated.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }
    private void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();
    private void CueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelected();
    private void UpdateSelected() => SelectedText.Text = (CueGrid.SelectedItem as EditableCue)?.Translated ?? "—";

    private List<SubtitleCue> ReadDraft()
    {
        // Include all rows, including those hidden by search.
        var cues = _rows.Select(row => new SubtitleCue(row.Start, row.End, row.Translated.Trim())).ToList();
        SrtParser.Serialize(cues); // Validate empty text/blank lines before either mutation.
        return cues;
    }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_apply(ReadDraft()))
            {
                StudioDialog.Show(this, "Bu slotun dosyası değişti veya yeni çeviri başladı. Güncel önizlemeyi açın. Düzeltmelerinizi ayrı SRT olarak kaydedebilirsiniz.", "Slot değişti", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _dirty = false;
            EditorStatus.Text = "Düzeltmeler slota uygulandı. Diske kaydetmek için SRT kaydet'i kullanın.";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cues = ReadDraft();
            var dialog = new SaveFileDialog { Filter = "SRT (*.srt)|*.srt", FileName = _suggestedName, InitialDirectory = _initialDirectory ?? "", DefaultExt = ".srt", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, SrtParser.Serialize(cues), new UTF8Encoding(true));
            _dirty = false;
            EditorStatus.Text = "Çevrilmiş SRT kaydedildi. Yayına aktarmak için Slota uygula'yı kullanın.";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ShowError(Exception ex) => StudioDialog.Show(this, ex.Message, "Altyazı hatası", MessageBoxButton.OK, MessageBoxImage.Error);
}

public sealed class EditableCue : INotifyPropertyChanged
{
    public int Number { get; }
    public TimeSpan Start { get; }
    public TimeSpan End { get; }
    public string Timing => $"{SrtParser.Timecode(Start)} → {SrtParser.Timecode(End)}";
    public string Original { get; }
    private string _translated;
    public string Translated
    {
        get => _translated;
        set { if (_translated == value) return; _translated = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Translated))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public EditableCue(int number, SubtitleCue cue, string original)
    { Number = number; Start = cue.Start; End = cue.End; Original = original; _translated = cue.Text; }
}
