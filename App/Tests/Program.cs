using System.Text.Json;
using VmixSubtitleController;
using VmixSubtitleController.Output;

static void Check(bool result, string name)
{
    if (!result) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}

static string Manifest(string version, string downloadUrl = "https://releases.example/setup.exe") =>
    JsonSerializer.Serialize(new { version, downloadUrl, added = new[] { "Yeni" }, @fixed = new[] { "Düzeltildi" } });

var update = ReleaseUpdate.Parse(Manifest("0.0100"), "0.0099");
Check(update is { Version: "0.0100", Added: ["Yeni"], Fixed: ["Düzeltildi"] }, "Newer version and notes");
Check(ReleaseUpdate.Parse(Manifest("0.0073"), "0.0073") == null, "Current version is silent");
Check(ReleaseUpdate.Parse(Manifest("0.0072"), "0.0073") == null, "Downgrade is ignored");
Check(ReleaseUpdate.Parse(Manifest("0.73"), "0.0073") == null, "Malformed version is ignored");
Check(ReleaseUpdate.Parse(Manifest("0.0074", "file:///C:/setup.exe"), "0.0073") == null, "Non-HTTPS download rejected");
Check(ReleaseUpdate.Parse(Manifest("0.0074", "https://name@releases.example/setup.exe"), "0.0073") == null, "Embedded URL credentials rejected");
Check(ReleaseUpdate.Parse("invalid JSON", "0.0073") == null, "Malformed manifest is ignored");
Check(await ReleaseUpdate.CheckAsync(null, "0.0073", default) == null, "Missing feed does not request updates");

static string GitHubRelease(string url, string? digest = null, bool prerelease = false) =>
    JsonSerializer.Serialize(new
    {
        tag_name = "v0.0078", draft = false, prerelease, body = "Sürüm notları",
        assets = new[] { new { name = "im-too-lazy-0.0078-setup.exe", state = "uploaded", size = 1000, browser_download_url = url, digest } }
    });

const string installer = "https://github.com/Alpkunsama/im-too-lazy/releases/download/v0.0078/im-too-lazy-0.0078-setup.exe";
var digest = new string('a', 64);
var github = GitHubRelease(installer, "sha256:" + digest);
Check(ReleaseUpdate.ParseGitHubRelease(github, "0.0077") is { Version: "0.0078", Size: 1000, Notes: "Sürüm notları" } release &&
    release.Sha256 == digest, "GitHub update uses release notes and installer checksum");
Check(ReleaseUpdate.ParseGitHubRelease(github, "0.0078") == null, "Same GitHub release does not offer an update");
foreach (var (json, name) in new[]
{
    (GitHubRelease(installer), "Missing GitHub digest rejected"),
    (GitHubRelease(installer.Replace("Alpkunsama", "other-owner"), "sha256:" + digest), "Other GitHub repositories rejected"),
    (GitHubRelease(installer, "sha256:" + digest, prerelease: true), "GitHub prereleases rejected")
})
{
    var rejected = false;
    try { ReleaseUpdate.ParseGitHubRelease(json, "0.0077"); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, name);
}

var cues = SrtParser.Parse("1\r\n00:00:01,000 --> 00:00:02,500\r\nİlk satır\r\nİkinci satır\r\n");
Check(cues.Count == 1 && cues[0].End.TotalMilliseconds == 2500 &&
    cues.SequenceEqual(SrtParser.Parse(SrtParser.Serialize(cues))), "SRT timing and multiline Unicode round trip");

var durations = new Dictionary<string, double> { ["a"] = 20_000, ["b"] = 30_000 };
Check(PlaylistClock.Calculate(["a", "b", "a"], durations, 1, 5000) == new PlaylistClockResult(70_000, 25_000, 45_000),
    "Playlist total includes repeated videos");
Check(PlaylistClock.Calculate(["a", "unknown"], durations, 0, 5000) == new PlaylistClockResult(null, 5000, null),
    "Unknown duration does not produce a misleading countdown");
