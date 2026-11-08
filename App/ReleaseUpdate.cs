using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace VmixSubtitleController;

internal sealed record ReleaseUpdate(string Version, Uri Download, string[] Added, string[] Fixed)
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = 32 * 1024
    };

    public static async Task<ReleaseUpdate?> CheckAsync(string? feedUrl, string currentVersion, CancellationToken token)
    {
        if (!TryHttpsUri(feedUrl, out var feed)) return null;
        try
        {
            return Parse(await Client.GetStringAsync(feed, token), currentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    internal static ReleaseUpdate? Parse(string json, string currentVersion)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryVersion(currentVersion, out var current) ||
                !TryVersion(ReadText(root, "version"), out var latest) || latest <= current ||
                !TryHttpsUri(ReadText(root, "downloadUrl"), out var download))
                return null;

            return new ReleaseUpdate(ReadText(root, "version")!, download,
                ReadNotes(root, "added"), ReadNotes(root, "fixed"));
        }
        catch (JsonException) { return null; }
    }

    private static bool TryVersion(string? value, out Version version)
    {
        if (System.Version.TryParse(value, out var parsed) && parsed.Build == -1 &&
            value == $"{parsed.Major}.{parsed.Minor:D4}")
        {
            version = parsed;
            return true;
        }
        version = new Version();
        return false;
    }

    private static bool TryHttpsUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps && parsed.Host.Length > 0 && parsed.UserInfo.Length == 0)
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    private static string? ReadText(JsonElement root, string key) =>
        root.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private static string[] ReadNotes(JsonElement root, string key) =>
        root.TryGetProperty(key, out var notes) && notes.ValueKind == JsonValueKind.Array
            ? notes.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim()).Where(note => note.Length > 0)
                .Take(20).Select(note => note.Length <= 400 ? note : note[..400] + "…").ToArray()
            : [];
}
