using System.IO;
using System.Text;
namespace VmixSubtitleController;
internal static class TranslatedSubtitleStore
{
    internal static string Save(string language, IReadOnlyList<SubtitleCue> cues, string? storageFolder = null)
    {
        var content = SrtParser.Serialize(cues);
        var folder = storageFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "translations");
        Directory.CreateDirectory(folder);
        // Stable content identity avoids numbered copies and remains valid across upgrades.
        var identity = language + "\n" + content;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var path = Path.Combine(folder, hash + "." + SrtParser.LanguageTag(language) + ".srt");
        if (File.Exists(path) && File.ReadAllText(path) == content) return path;
        var temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(true));
            File.Move(temp, path, true);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

}
