using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace VmixSubtitleController;

internal sealed record ReleaseUpdate(string Version, Uri Download, string[] Added, string[] Fixed,
    string? Notes = null, string? Sha256 = null, long Size = 0)
{
    private const string Repository = "Alpkunsama/im-too-lazy";
    private const long MaxInstallerSize = 1024L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 256 * 1024
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("im-too-lazy-updater/1.0");
        return client;
    }

    public static async Task<ReleaseUpdate?> CheckGitHubAsync(string currentVersion, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var json = await Client.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token);
        return ParseGitHubRelease(json, currentVersion);
    }

    public static async Task<ReleaseUpdate?> CheckAsync(string? feedUrl, string currentVersion, CancellationToken token)
    {
        if (!TryHttpsUri(feedUrl, out var feed)) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return Parse(await Client.GetStringAsync(feed, timeout.Token), currentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    internal static ReleaseUpdate? ParseGitHubRelease(string json, string currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !TryVersion(currentVersion, out var current) ||
            ReadText(root, "tag_name") is not { } tag || !tag.StartsWith('v') ||
            !TryVersion(tag[1..], out var latest) ||
            !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out var preview) || preview.ValueKind != JsonValueKind.False)
            throw new InvalidDataException("GitHub sürüm bilgisi geçersiz.");
        if (latest <= current) return null;

        var version = tag[1..];
        var name = $"im-too-lazy-{version}-setup.exe";
        var download = new Uri($"https://github.com/{Repository}/releases/download/{tag}/{name}");
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || ReadText(asset, "name") != name ||
                    ReadText(asset, "state") != "uploaded" ||
                    ReadText(asset, "browser_download_url") != download.AbsoluteUri ||
                    !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number ||
                    !size.TryGetInt64(out var length) || length is <= 0 or > MaxInstallerSize ||
                    ReadText(asset, "digest") is not { Length: 71 } digest ||
                    !digest.StartsWith("sha256:", StringComparison.Ordinal) || !digest[7..].All(Uri.IsHexDigit))
                    continue;
                var notes = ReadText(root, "body")?.Trim();
                if (notes?.Length > 6000) notes = notes[..6000] + "…";
                return new(version, download, [], [], notes, digest[7..], length);
            }
        }
        throw new InvalidDataException("Yeni sürümün doğrulanabilir Windows kurucusu bulunamadı.");
    }

    public async Task<string> DownloadInstallerAsync(CancellationToken token)
    {
        if (Sha256 is null || Size is <= 0 or > MaxInstallerSize)
            throw new InvalidDataException("Kurulum dosyasının doğrulama bilgisi eksik.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        token = timeout.Token;
        var file = Path.Combine(Path.GetTempPath(), $"im-too-lazy-{Guid.NewGuid():N}-setup.exe");
        try
        {
            using var response = await OpenInstallerAsync(Download, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != Size)
                throw new InvalidDataException("Kurulum dosyasının boyutu eşleşmedi.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    received += read;
                    if (received > Size) throw new InvalidDataException("Kurulum dosyasının boyutu eşleşmedi.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (received != Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Kurulum dosyasının SHA-256 doğrulaması başarısız.");
            }
            await File.WriteAllTextAsync(file + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", token);
            return file;
        }
        catch
        {
            if (File.Exists(file)) File.Delete(file);
            throw;
        }
    }

    private static async Task<HttpResponseMessage> OpenInstallerAsync(Uri url, CancellationToken token)
    {
        for (var redirects = 0; redirects < 5; redirects++)
        {
            var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.IsSuccessStatusCode) return response;
            using (response)
            {
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) ||
                    response.Headers.Location is not { } location ||
                    !Uri.TryCreate(url, location, out var next) || next.Scheme != Uri.UriSchemeHttps ||
                    !next.IsDefaultPort || next.UserInfo.Length != 0 ||
                    next.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new HttpRequestException("GitHub kurulum dosyasını sunamadı.", null, response.StatusCode);
                url = next;
            }
        }
        throw new HttpRequestException("GitHub indirme yönlendirmesi tamamlanamadı.");
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
