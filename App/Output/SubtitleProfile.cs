using System.IO;
using System.Text.Json;

namespace VmixSubtitleController.Output;

public sealed record SubtitleProfile
{
    public int Version { get; init; } = 1;
    public int SlotCount { get; init; } = 4;
    public ProfileUi Ui {get;init;}=new();
    public string? SelectedPlaylistId {get;init;}
    public int SelectedSlot {get;init;}=1;
    public List<ProfileSubtitleSlot> Subtitles { get; init; } = new();
    public AppPreferences Preferences { get; init; } = new();
    public string OutputMode { get; init; } = "vmix";
    public string? VmixApi { get; init; }
    public string ObsHost { get; init; } = "127.0.0.1";
    public int ObsPort { get; init; } = 4455;
    public string? VmixTitleKey { get; init; }
    public string? ObsTitleKey { get; init; }
    public Dictionary<int, string?> VmixSlotInputs { get; init; } = new();
    public Dictionary<int, string?> ObsSlotInputs { get; init; } = new();
    public List<GlossaryEntry> Glossary { get; init; } = new();

    public List<string> ControlCardOrder {get;init;}=new();
    public bool ExplicitControlCards { get; init; }
    public List<VideoPlaylist> Playlists { get; init; } = new();

    public void Validate()
    {
        if(ControlCardOrder==null||ControlCardOrder.Count>10000||ControlCardOrder.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>100)||ControlCardOrder.Distinct().Count()!=ControlCardOrder.Count)throw new InvalidDataException("Control kart sırası geçersiz.");
        if (Version != 1) throw new InvalidDataException("Bu profil sürümü desteklenmiyor.");
        if (SlotCount is < 1 or > 100) throw new InvalidDataException("Profildeki çalışma alanı sayısı geçersiz.");
        if (Playlists == null || Playlists.Any(list => list == null)) throw new InvalidDataException("Profildeki oynatma listeleri geçersiz.");
        foreach (var list in Playlists) list.Validate();
        if (Playlists.Select(list => list.Id).Distinct().Count() != Playlists.Count) throw new InvalidDataException("Oynatma listesi kimlikleri tekrarlanamaz.");
        if(Subtitles == null || Subtitles.Any(x=>x==null || x.Number<1 || x.Number>SlotCount || x.SourceCues==null || x.Cues==null || string.IsNullOrWhiteSpace(x.TargetLanguage) || x.TargetLanguage.Length>80 || x.RequestedLanguage?.Length>80)) throw new InvalidDataException("Profil altyazıları geçersiz.");
        if(Subtitles.Select(x=>x.Number).Distinct().Count()!=Subtitles.Count)throw new InvalidDataException("Profil altyazı slotları tekrarlanamaz.");
        foreach(var slot in Subtitles)foreach(var cue in slot.SourceCues.Concat(slot.Cues))if(cue==null||cue.Text==null||cue.Start<TimeSpan.Zero||cue.End<cue.Start)throw new InvalidDataException("Profil altyazı zamanları geçersiz.");
        if(Ui==null)throw new InvalidDataException("Profil görünüm ayarları geçersiz.");Ui.Validate();
        Preferences.Validate();
        if (OutputMode is not ("vmix" or "obs")) throw new InvalidDataException("Profildeki çıkış türü geçersiz.");
        if (ObsPort is < 1 or > 65535) throw new InvalidDataException("OBS portu 1 ile 65535 arasında olmalı.");
        if (VmixSlotInputs.Keys.Any(x => x < 1 || x > SlotCount) || ObsSlotInputs.Keys.Any(x => x < 1 || x > SlotCount))
            throw new InvalidDataException("Profildeki slot eşleştirmesi geçersiz.");
        if (Glossary.Count > 5000) throw new InvalidDataException("Profil sözlüğü en fazla 5000 terim içerebilir.");
        foreach (var entry in Glossary) entry.Validate();
        if (Glossary.GroupBy(x => (x.TargetLanguage, x.SourceTerm), StringTupleComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw new InvalidDataException("Profil sözlüğünde aynı dil ve kaynak terim birden fazla kez bulunamaz.");
    }

    public static SubtitleProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<SubtitleProfile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Profil dosyası okunamadı.");
        profile.Validate();
        return profile;
    }

    public void Save(string path)
    {
        Validate();
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Profil klasörü bulunamadı.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed record GlossaryEntry(string SourceTerm, string TargetTerm, string TargetLanguage)
{
    public GlossaryPresetOrigin? Preset { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceTerm) || SourceTerm.Trim().Length > 120)
            throw new InvalidDataException("Sözlük kaynak terimi 1 ile 120 karakter arasında olmalı.");
        if (string.IsNullOrWhiteSpace(TargetTerm) || TargetTerm.Trim().Length > 120)
            throw new InvalidDataException("Sözlük karşılığı 1 ile 120 karakter arasında olmalı.");
        if (string.IsNullOrWhiteSpace(TargetLanguage) || TargetLanguage.Trim().Length > 80)
            throw new InvalidDataException("Sözlük hedef dili 1 ile 80 karakter arasında olmalı.");
        Preset?.Validate();
    }
}

public sealed record GlossaryPresetOrigin(string Id, string Language)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 40 || Id.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
            string.IsNullOrWhiteSpace(Language) || Language.Length > 80)
            throw new InvalidDataException("Sözlük preseti geçersiz.");
    }
}

internal sealed class StringTupleComparer : IEqualityComparer<(string TargetLanguage, string SourceTerm)>
{
    public static readonly StringTupleComparer OrdinalIgnoreCase = new();
    public bool Equals((string TargetLanguage, string SourceTerm) x, (string TargetLanguage, string SourceTerm) y) =>
        string.Equals(x.TargetLanguage, y.TargetLanguage, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(x.SourceTerm, y.SourceTerm, StringComparison.OrdinalIgnoreCase);
    public int GetHashCode((string TargetLanguage, string SourceTerm) value) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.TargetLanguage), StringComparer.OrdinalIgnoreCase.GetHashCode(value.SourceTerm));
}

public sealed record PlaylistVideo(string Key, string Label);
public sealed record VideoPlaylist(string Id, string Name, string OutputMode, List<PlaylistVideo> Videos)
{
    public bool SingleVideo {get;init;}
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || OutputMode is not ("vmix" or "obs") || Videos == null || Videos.Any(video => video == null || string.IsNullOrWhiteSpace(video.Key)))
            throw new InvalidDataException("Oynatma listesi bilgileri geçersiz.");
    }
}

public sealed record ProfileSubtitleSlot
{
    public int Number {get;init;}
    public string? SrtPath {get;init;}
    public string? OutputPath {get;init;}
    public string? RequestedLanguage {get;init;}
    public bool UseSourceSubtitles {get;init;}
    public string TargetLanguage {get;init;}="Türkçe";
    public string? SourceLanguageCode {get;init;}
    public bool HasManualEdits {get;init;}
    public List<SubtitleCue> SourceCues {get;init;}=new();
    public List<SubtitleCue> Cues {get;init;}=new();
}

public sealed record ProfileUi
{
 public string replaySettings {get;init;}="";
 public int previewFontSize {get;init;}=16;
 public bool collapsed {get;init;}
 public string chatTarget {get;init;}="Türkçe";
 public string glossaryLanguage {get;init;}="Türkçe";
 public string cameraDevice {get;init;}="";
 public bool cameraActive {get;init;}
 public bool cameraPaused {get;init;}
 public string omtSource {get;init;}="";
 public string broadcastCategory {get;init;}="broadcast";
 public string settingsCategory {get;init;}="general";
 public void Validate(){if(replaySettings==null||replaySettings.Length>300000||previewFontSize is <14 or >36 || chatTarget==null||chatTarget.Length>80||glossaryLanguage==null||glossaryLanguage.Length>80||cameraDevice==null||cameraDevice.Length>512||omtSource==null||omtSource.Length>1024||broadcastCategory is not("broadcast" or "workspace" or "playlist")||settingsCategory is not("general" or "profile" or "connections" or "appearance" or "translation"))throw new InvalidDataException("Profil görünüm seçimleri geçersiz.");}
}
