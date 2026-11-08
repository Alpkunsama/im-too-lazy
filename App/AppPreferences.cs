using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VmixSubtitleController;

public sealed record AppPreferences
{
    public static readonly string[] ThemeIds = { "graphite", "violet", "teal", "wine", "amber", "light", "cosmic", "sakura", "cyberpunk", "matrix", "arcade", "aurora" };
    public static readonly string[] InterfaceLanguageIds = { "tr", "en", "fr", "es", "ja", "ko", "de", "zh", "pt", "ru" };
    public static readonly string[] Languages = { "Türkçe", "İngilizce", "Almanca", "Fransızca", "İspanyolca", "İtalyanca", "Portekizce", "Hollandaca", "Lehçe", "Ukraynaca", "Rusça", "Arapça", "Farsça", "Japonca", "Korece", "Çince (Basitleştirilmiş)", "Çince (Geleneksel)", "Hintçe", "İsveççe", "Yunanca" };
    public const string DefaultTheme = "https://www.youtube.com/watch?v=q95PHFczcVA";
    public string TranslationPreset {get;init;}="normal";
    public static string PresetInstructions(string preset)=>preset switch {
        "normal"=>"",
        "friendly"=>" Use a warm, friendly, conversational tone and natural everyday phrasing.",
        "corporate"=>" Use a professional, formal corporate tone with clear precise phrasing.",
        "esports"=>" Use energetic esports commentary style and familiar gaming terminology where appropriate. Do not invent game events or slang unrelated to the source.",
        _=>throw new ArgumentException("Geçersiz çeviri preseti.")
    };
    public string TargetLanguage { get; init; } = "Türkçe";
    public string InterfaceLanguage { get; init; } = "en";
    public string? ModelSlug { get; init; }
    public bool ThemeEnabled { get; init; } = false;
    public bool DarkMode { get; init; } = true;
    public string ColorTheme { get; init; } = "graphite";
    public string ThemeUrl { get; init; } = DefaultTheme;
    public double ThemeDarkness { get; init; } = 0.62;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "settings.json");
    public static AppPreferences Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        var value = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(SettingsPath)) ?? throw new InvalidDataException("Ayarlar okunamadı.");
        value.Validate(); return value;
    }
    public void Validate()
    {
        _=PresetInstructions(TranslationPreset);
        if (!ThemeIds.Contains(ColorTheme)) throw new ArgumentException("Geçersiz renk teması.");
        if (!InterfaceLanguageIds.Contains(InterfaceLanguage)) throw new ArgumentException("Desteklenmeyen uygulama dili.");
        if (string.IsNullOrWhiteSpace(TargetLanguage) || TargetLanguage.Length > 80) throw new ArgumentException("Hedef dil adını girin (en fazla 80 karakter).");
        if (!double.IsFinite(ThemeDarkness) || ThemeDarkness < 0.15 || ThemeDarkness > 0.9) throw new ArgumentException("Arka plan karartma değeri geçersiz.");
        if (ThemeEnabled && !YouTubeTheme.TryVideoId(ThemeUrl, out _)) throw new ArgumentException("Geçerli bir YouTube video bağlantısı girin.");
    }
    public void Save()
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, SettingsPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class YouTubeTheme
{
    public static bool TryVideoId(string? url, out string id)
    {
        id = "";
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https") return false;
        var host = uri.Host.ToLowerInvariant();
        if (host == "youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "www.youtube-nocookie.com")
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length == 2 && segments[0] is "embed" or "shorts" or "live") id = segments[1];
            else if (uri.AbsolutePath == "/watch")
            {
                foreach (var part in uri.Query.TrimStart('?').Split('&'))
                {
                    var pair = part.Split('=', 2);
                    if (pair.Length == 2 && pair[0] == "v") { id = Uri.UnescapeDataString(pair[1]); break; }
                }
            }
        }
        return Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$");
    }

    public static string BuildHtml(string videoId)
    {
        if (!Regex.IsMatch(videoId, "^[A-Za-z0-9_-]{11}$")) throw new ArgumentException("Video kimliği geçersiz.");
        return """
<!doctype html><html><head><meta charset="utf-8"><meta name="referrer" content="strict-origin-when-cross-origin">
<style>html,body{margin:0;width:100%;height:100%;overflow:hidden;background:#091017}#player{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:max(100vw,177.78vh);height:max(100vh,56.25vw);pointer-events:none}</style></head><body><div id="player"></div>
<script>
let player;function report(type,code){if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage({type,code})}
function onYouTubeIframeAPIReady(){player=new YT.Player('player',{host:'https://www.youtube-nocookie.com',videoId:'
""" + videoId + """
',playerVars:{autoplay:1,mute:1,controls:0,disablekb:1,loop:1,playlist:'
""" + videoId + """
',playsinline:1,rel:0,origin:location.origin},events:{onReady:e=>{e.target.mute();e.target.playVideo()},onStateChange:e=>{if(e.data===1)report('theme-playing',0);if(e.data===0){e.target.seekTo(0);e.target.playVideo()}},onError:e=>report('theme-error',e.data)}})}
let script=document.createElement('script');script.src='https://www.youtube.com/iframe_api';script.onerror=()=>report('theme-error','network');document.head.appendChild(script);
setTimeout(()=>{if(!player||typeof player.getPlayerState!=='function'||player.getPlayerState()!==1)report('theme-waiting',0)},20000);
</script></body></html>
""";
    }
}
