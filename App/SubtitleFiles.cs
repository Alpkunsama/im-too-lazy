using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VmixSubtitleController;

public record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

public static class SrtParser
{
    public static List<SubtitleCue> Parse(string content)
    {
        content = content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (content.Length == 0) throw new FormatException("SRT dosyası boş.");
        var blocks = Regex.Split(content, @"\n[\t ]*\n+");
        var result = new List<SubtitleCue>();
        foreach (var block in blocks)
        {
            var lines = block.Trim().Split('\n');
            var timeIndex = Array.FindIndex(lines, l => l.Contains("-->"));
            if (timeIndex < 0) throw new FormatException($"SRT bloğunda zaman kodu yok: {result.Count + 1}");
            var parts = lines[timeIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TryTime(parts[0], out var start) || !TryTime(parts[1], out var end) || end <= start)
                throw new FormatException($"Geçersiz zaman kodu: {lines[timeIndex]}");
            var text = string.Join("\n", lines.Skip(timeIndex + 1)).Trim();
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Boş altyazı metni bulundu.");
            result.Add(new SubtitleCue(start, end, text));
        }
        return result.OrderBy(x => x.Start).ToList();
    }

    private static bool TryTime(string value, out TimeSpan time)
    {
        time = default;
        var match = Regex.Match(value.Trim(), @"^(\d{2,}):(\d{2}):(\d{2})[,.](\d{3})$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var hours) || hours > 9999) return false;
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var millis = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        if (minutes >= 60 || seconds >= 60) return false;
        time = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(millis);
        return true;
    }

    public static string LanguageTag(string language) => language.Trim().ToLowerInvariant() switch
    {
        "türkçe" or "turkish" => "tr", "ingilizce" or "english" => "en", "almanca" or "german" => "de",
        "çince" or "chinese" => "zh", "fransızca" or "french" => "fr", "ispanyolca" or "spanish" => "es",
        _ => "translated"
    };

    public static string Timecode(TimeSpan time) =>
        $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";

    public static string Serialize(IReadOnlyList<SubtitleCue> cues)
    {
        var output = new StringBuilder();
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            var text = cue.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (cue.Start < TimeSpan.Zero || cue.End <= cue.Start || string.IsNullOrWhiteSpace(text) || text.Split('\n').Any(string.IsNullOrWhiteSpace))
                throw new FormatException($"{i + 1}. altyazı boş veya geçersiz. Boş satırları kaldırın.");
            output.Append(i + 1).Append("\r\n").Append(Timecode(cue.Start)).Append(" --> ").Append(Timecode(cue.End))
                .Append("\r\n").Append(text.Replace("\n", "\r\n")).Append("\r\n\r\n");
        }
        return output.ToString();
    }
}
