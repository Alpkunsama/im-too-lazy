using System.IO;
using libomtnet;
using System.Reflection;
using System.Net;
using System.Xml.Linq;

namespace VmixSubtitleController;

public partial class MainWindow
{
    private sealed record VmixHost(string address, string name, string ip);
    private List<VmixHost> _vmixFoundHosts = new();
    private bool _vmixScanning;

    private static string NormalizeVmixAddress(string address)
    {
        address = address.Trim();
        if (address.Length == 0) throw new ArgumentException("vMix bilgisayarının IP adresini veya adını girin.");
        if (!address.Contains("://")) address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Geçerli bir vMix IP adresi veya HTTP adresi girin.");
        var builder = new UriBuilder(uri) { Path = "/api/", Query = "", Fragment = "" };
        // Bare host/IP uses vMix's default Web Controller port.
        if (uri.IsDefaultPort && !System.Text.RegularExpressions.Regex.IsMatch(uri.Authority, @":\d+$")) builder.Port = 8088;
        return builder.Uri.AbsoluteUri;
    }

    private async Task<bool> ProbeVmixAsync(string address, CancellationToken token)
    {
        using var response = await _http.GetAsync(address, token);
        if (!response.IsSuccessStatusCode) return false;
        var xml = await response.Content.ReadAsStringAsync(token);
        return XDocument.Parse(xml).Root?.Name.LocalName.Equals("vmix", StringComparison.OrdinalIgnoreCase) == true;
    }

    private async Task ConnectVmixAddressAsync(string address)
    {
        if (_refreshing) throw new InvalidOperationException("Bağlantı işlemi sürüyor.");
        var target = NormalizeVmixAddress(address);
        _refreshing = true;
        try
        {
            GlobalStatus.Text = "vMix bağlantısı kontrol ediliyor…";
            if (!await ProbeVmixAsync(target, _lifetime.Token))
                throw new InvalidOperationException("Bu adreste vMix API bulunamadı. vMix Web Controller ve güvenlik duvarı ayarlarını kontrol edin.");
            await ClearCurrentOutputTextAsync();
            SaveCurrentOutputMappings();
            ResetPlaylistPlayback();
            _apiBase = target;
            _outputMode = "vmix";
            _subtitleOutput = _vmixSubtitleOutput;
            InvalidateSubtitle();
            await RefreshInputsAsync();
        }
        finally { _refreshing = false; }
    }

    private static async Task<string?> ResolveVmixComputerNameAsync(string host, CancellationToken token)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nbtstat.exe"),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-A");
        process.StartInfo.ArgumentList.Add(ip.ToString());
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(2), token);
            var output = await outputTask;
            await errorTask;
            // The workstation name is registered as NetBIOS suffix 00; prefer server suffix 20.
            foreach (var suffix in new[] { "20", "00" })
            {
                var match = System.Text.RegularExpressions.Regex.Match(output,
                    @"^\s*([^\s]+)\s+<" + suffix + @">\s+([^\r\n]+)",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                if (match.Success && !System.Text.RegularExpressions.Regex.IsMatch(match.Groups[2].Value,
                    "GROUP|GRUP", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return match.Groups[1].Value.Trim();
            }
        }
        catch { }
        finally { try { if (!process.HasExited) process.Kill(); } catch { } }
        return null;
    }

    private static Dictionary<string, string> GetOmtComputerNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var discovery = OMTDiscovery.GetInstance();
            // GetAddressesInternal excludes Discovered entries (remote sources).
            // Resolve each public discovery label through the library's locked lookup instead.
            var lookup = typeof(OMTDiscovery).GetMethod("FindByFullName", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            foreach (var label in discovery.GetAddresses())
            {
                if (lookup?.Invoke(discovery, new object[] { label }) is not OMTAddress source ||
                    string.IsNullOrWhiteSpace(source.MachineName)) continue;
                foreach (var ip in source.Addresses)
                    names.TryAdd((ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString(), source.MachineName.Trim());
            }
        }
        catch { /* Keep DNS/NetBIOS discovery available if OMT is unavailable. */ }
        return names;
    }

    private async Task ScanVmixNetworkAsync()
    {
        if (_vmixScanning) return;
        _vmixScanning = true;
        SendWebState(true);
        GlobalStatus.Text = "Ağdaki vMix bilgisayarları aranıyor…";
        try
        {
            await RefreshOmtSourcesAsync();
            var omtNames = GetOmtComputerNames();
            var local = GetLocalIPv4Addresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = GetSubnetCandidates().Concat(new[] { "http://127.0.0.1:8088/api/" })
                .Concat(local.Select(ip => $"http://{ip}:8088/api/"));
            candidates = candidates.Concat(omtNames.Keys.Where(ip => IPAddress.TryParse(ip, out var parsed) && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(ip => $"http://{ip}:8088/api/"));
            if (!string.IsNullOrWhiteSpace(_apiBase)) candidates = candidates.Append(_apiBase);
            using var gate = new SemaphoreSlim(32);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var found = await Task.WhenAll(candidates.Distinct(StringComparer.OrdinalIgnoreCase).Select(async address =>
            {
                var acquired = false;
                try
                {
                    await gate.WaitAsync(timeout.Token); acquired = true;
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    probe.CancelAfter(700);
                    if (!await ProbeVmixAsync(address, probe.Token)) return null;
                    var host = new Uri(address).Host;
                    string name = host;
                    if (host == "127.0.0.1" || host == "localhost" || local.Contains(host)) name = Environment.MachineName;
                    else if (omtNames.TryGetValue(host, out var advertisedName)) name = advertisedName;
                    else
                    {
                        try { name = (await Dns.GetHostEntryAsync(host).WaitAsync(TimeSpan.FromMilliseconds(800), timeout.Token)).HostName; }
                        catch { }
                    }
                    if (name == host || IPAddress.TryParse(name, out _))
                    {
                        var netbiosName = await ResolveVmixComputerNameAsync(host, timeout.Token);
                        name = netbiosName ?? "İsim alınamadı";
                    }
                    return new VmixHost(address, name, host == "localhost" ? "127.0.0.1" : host);
                }
                catch { return null; }
                finally { if (acquired) gate.Release(); }
            }));
            var latestOmtNames = GetOmtComputerNames();
            _vmixFoundHosts = found.Where(host => host != null).Cast<VmixHost>()
                .Where(host => !local.Contains(new Uri(host.address).Host))
                .Select(host => latestOmtNames.TryGetValue(host.ip, out var advertisedName) && host.ip != "127.0.0.1" ? host with { name = advertisedName } : host)
                .OrderBy(host => host.name, StringComparer.CurrentCultureIgnoreCase).ThenBy(host => host.address).ToList();
            GlobalStatus.Text = $"{_vmixFoundHosts.Count} vMix bilgisayarı bulundu. Bağlanmak için bilgisayar seçin.";
        }
        finally { _vmixScanning = false; }
    }
}

