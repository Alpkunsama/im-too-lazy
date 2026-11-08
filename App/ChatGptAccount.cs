using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace VmixSubtitleController;

public sealed class ChatGptAccount : IChatGptSession, IDisposable
{
    private const string Issuer = "https://auth.openai.com";
    private const string Resource = "https://api.openai.com/v1";
    private const string AgentName = "im too lazy";
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(45) };
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly string _directory;
    private AccountProfile? _profile;
    private Registration? _registration;
    private readonly string _hostId;
    public string DisplayName => _profile?.Email ?? "ChatGPT bağlı değil";
    public bool CanTranslate => _profile != null && HasUsagePermission(_profile.Scopes);
    public bool HasProfile => _profile != null;
    public string? StartupWarning { get; }
    public bool WelcomeShown { get; private set; }

    public ChatGptAccount()
    {
        _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "chatgpt");
        Directory.CreateDirectory(_directory);
        var hostFile = Path.Combine(_directory, "host-id.txt");
        _hostId = File.Exists(hostFile) ? File.ReadAllText(hostFile).Trim() : "urn:uuid:" + Guid.NewGuid();
        if (!Uri.TryCreate(_hostId, UriKind.Absolute, out _)) throw new InvalidDataException("ChatGPT cihaz kimliği geçersiz.");
        if (!File.Exists(hostFile)) AtomicWrite(hostFile, Encoding.UTF8.GetBytes(_hostId));
        WelcomeShown = File.Exists(Path.Combine(_directory, "welcome-shown"));
        try { _registration = ReadProtected<Registration>("registration.bin"); }
        catch { _registration = null; StartupWarning = "ChatGPT uygulama kaydı okunamadı. Yeniden girişte yeni kayıt gerekebilir."; }
        try
        {
            _profile = ReadProtected<AccountProfile>("account.bin");
            if (_profile != null && _profile.ClientId != _registration?.ClientId)
            {
                _profile = null;
                StartupWarning = "Kaydedilmiş ChatGPT oturumu uygulama kaydıyla eşleşmiyor. Yeniden giriş yapın.";
            }
        }
        catch
        {
            _profile = null;
            StartupWarning = "Kaydedilmiş ChatGPT oturumu okunamadı. Tekrar giriş yapın; uygulama kaydı korundu.";
        }
    }

    public void MarkWelcomeShown()
    { AtomicWrite(Path.Combine(_directory, "welcome-shown"), new byte[] { 1 }); WelcomeShown = true; }

    public async Task SignInAsync(CancellationToken cancellation)
    {
        await _sessionGate.WaitAsync(cancellation);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var token = timeout.Token;
            var state = RandomValue(); var nonce = RandomValue(); var verifier = RandomValue(64);
            var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            // Use a loopback socket so Windows sign-in needs no HTTP URL ACL or administrator rights.
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var prefix = $"http://127.0.0.1:{port}/";
            var redirect = prefix + "auth/callback";
            using var closeListener = token.Register(() => listener.Stop());
            var old = _registration;
            var clientId = old?.ClientId ?? "dynamic_agent_client";
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["ext_agent_host_id"] = _hostId,
                ["response_type"] = "code", ["redirect_uri"] = redirect,
                ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
                ["resource"] = Resource, ["state"] = state, ["nonce"] = nonce,
                ["code_challenge_method"] = "S256", ["code_challenge"] = challenge
            };
            if (old == null) query["agent_name_hint"] = AgentName;
            else if (_profile != null) query["id_token_hint"] = _profile.IdToken;
            var url = Issuer + "/api/accounts/authorize?" + string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            IsolatedLoginBrowser.Open(url);
            string code; string issuedClient;
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(token);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var stream = client.GetStream();
                var returned = await ReadCallbackAsync(stream, prefix, requestTimeout.Token);
                if (returned == null)
                { await BrowserReply(stream, 404, "Bu adres kullanılamaz.", requestTimeout.Token); continue; }
                if (!FixedEqual(returned.GetValueOrDefault("state", ""), state))
                { await BrowserReply(stream, 400, "Giriş isteği eşleşmedi. Programdaki giriş işlemini kullanın.", requestTimeout.Token); continue; }
                if (!string.IsNullOrEmpty(returned.GetValueOrDefault("error")))
                {
                    await BrowserReply(stream, 400, "ChatGPT bağlantısına izin verilmedi. Programa dönebilirsiniz.", requestTimeout.Token);
                    throw new InvalidOperationException("ChatGPT giriş izni verilmedi. Çeviri için ChatGPT plan kullanımına izin verin.");
                }
                code = returned.GetValueOrDefault("code", "");
                issuedClient = returned.GetValueOrDefault("client_id") ?? (old?.ClientId ?? "");
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(issuedClient) || issuedClient == "dynamic_agent_client" ||
                    (old != null && issuedClient != old.ClientId))
                {
                    await BrowserReply(stream, 400, "Giriş tamamlanamadı. Programa dönüp yeniden deneyin.", requestTimeout.Token);
                    throw new InvalidOperationException("ChatGPT giriş yanıtı geçersiz.");
                }
                await BrowserReply(stream, 200, "ChatGPT giriş yanıtı alındı. Programa dönebilirsiniz; bağlantı orada doğrulanacak.", requestTimeout.Token);
                break;
            }
            // Retain the issued registration if code exchange fails, but do not replace active credentials yet.
            var registration = new Registration(issuedClient, old?.Subject);
            if (old == null) { WriteProtected("registration.bin", registration); _registration = registration; }
            using var response = await _http.PostAsync(Issuer + "/api/accounts/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = issuedClient,
                ["code"] = code, ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["resource"] = Resource
            }), token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("ChatGPT bağlantısı doğrulanamadı. Yeniden giriş yapın.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var idToken = RequiredString(body.RootElement, "id_token");
            var identity = await ValidateIdentityAsync(idToken, issuedClient, nonce, token);
            if (old?.Subject != null && old.Subject != identity.Subject)
                throw new InvalidOperationException("Bu kayıt farklı bir ChatGPT hesabına ait. Önce çıkış yapıp hesap kaydını sıfırlayın.");
            var profile = ParseProfile(body.RootElement, issuedClient, identity.Subject, identity.Email, idToken, null);
            _registration = new Registration(issuedClient, identity.Subject);
            WriteProtected("registration.bin", _registration);
            SaveProfile(profile);
        }
        catch (SocketException) when (cancellation.IsCancellationRequested)
        { throw new OperationCanceledException(cancellation); }
        finally { _sessionGate.Release(); }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellation)
    {
        await _sessionGate.WaitAsync(cancellation);
        try
        {
            var profile = _profile ?? throw new InvalidOperationException("Önce Continue with ChatGPT ile hesabınızı bağlayın.");
            if (!HasUsagePermission(profile.Scopes)) throw new InvalidOperationException("Giriş yapıldı fakat ChatGPT plan kullanımına izin verilmedi. Yeniden bağlanın.");
            if (profile.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return profile.AccessToken;
            if (string.IsNullOrEmpty(profile.RefreshToken)) throw new InvalidOperationException("ChatGPT oturumu sona erdi. Yeniden giriş yapın.");
            using var response = await _http.PostAsync(Issuer + "/api/accounts/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["client_id"] = profile.ClientId,
                ["refresh_token"] = profile.RefreshToken, ["resource"] = Resource
            }), cancellation);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("ChatGPT oturumu yenilenemedi. Yeniden giriş yapın.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (body.RootElement.TryGetProperty("id_token", out var id) && !string.IsNullOrEmpty(id.GetString()))
            {
                var identity = await ValidateIdentityAsync(id.GetString()!, profile.ClientId, null, cancellation);
                if (identity.Subject != profile.Subject) throw new InvalidOperationException("Yenilenen ChatGPT hesabı eşleşmiyor.");
            }
            var renewed = ParseProfile(body.RootElement, profile.ClientId, profile.Subject, profile.Email, profile.IdToken, profile);
            SaveProfile(renewed);
            if (!HasUsagePermission(renewed.Scopes)) throw new InvalidOperationException("ChatGPT plan izni artık etkin değil. Yeniden giriş yapın.");
            return renewed.AccessToken;
        }
        finally { _sessionGate.Release(); }
    }

    public async Task<bool> SignOutAsync(CancellationToken cancellation)
    {
        await _sessionGate.WaitAsync(cancellation);
        try
        {
            var confirmed = _profile == null || string.IsNullOrEmpty(_profile.RefreshToken);
            if (!confirmed)
            {
                try
                {
                    using var discovery = JsonDocument.Parse(await _http.GetStringAsync(Issuer + "/.well-known/openid-configuration", cancellation));
                    var endpoint = discovery.RootElement.GetProperty("revocation_endpoint").GetString();
                    if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "auth.openai.com")
                    {
                        using var response = await _http.PostAsync(uri, new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["token"] = _profile!.RefreshToken!, ["token_type_hint"] = "refresh_token", ["client_id"] = _profile.ClientId
                        }), cancellation);
                        confirmed = response.IsSuccessStatusCode;
                    }
                }
                catch { confirmed = false; }
            }
            _profile = null;
            var file = Path.Combine(_directory, "account.bin");
            if (File.Exists(file)) File.Delete(file);
            return confirmed;
        }
        finally { _sessionGate.Release(); }
    }

    // Explicitly reset a registration only when the user chooses to connect another account.
    public void ResetRegistration()
    {
        if (_profile != null) throw new InvalidOperationException("Önce ChatGPT hesabından çıkış yapın.");
        _registration = null;
        var path = Path.Combine(_directory, "registration.bin");
        if (File.Exists(path)) File.Delete(path);
    }

    private async Task<(string Subject, string Email)> ValidateIdentityAsync(string jwt, string clientId, string? nonce, CancellationToken cancellation)
    {
        var jwks = await _http.GetStringAsync(Issuer + "/.well-known/jwks.json", cancellation);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var claims = handler.ValidateToken(jwt, new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = clientId,
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
            ValidateIssuerSigningKey = true, IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
                SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512 },
            ClockSkew = TimeSpan.FromSeconds(5)
        }, out _);
        if (nonce != null && !FixedEqual(claims.FindFirst("nonce")?.Value ?? "", nonce))
            throw new SecurityTokenValidationException("ChatGPT giriş doğrulaması eşleşmedi.");
        var subject = claims.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject)) throw new SecurityTokenValidationException("ChatGPT hesap kimliği bulunamadı.");
        return (subject, claims.FindFirst("email")?.Value ?? "ChatGPT hesabı");
    }

    private static AccountProfile ParseProfile(JsonElement body, string client, string subject, string email, string id, AccountProfile? old)
    {
        var access = RequiredString(body, "access_token");
        if (body.TryGetProperty("token_type", out var type) && !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ChatGPT erişim belirteci türü geçersiz.");
        var scope = body.TryGetProperty("scope", out var scopeNode) ? scopeNode.GetString() ?? "" : old?.Scopes ?? "";
        var refresh = body.TryGetProperty("refresh_token", out var refreshNode) ? refreshNode.GetString() : old?.RefreshToken;
        var idToken = body.TryGetProperty("id_token", out var idNode) ? idNode.GetString() ?? id : id;
        var expires = body.TryGetProperty("expires_in", out var expiresNode) ? expiresNode.GetInt32() : 0;
        if (expires <= 0) throw new InvalidDataException("ChatGPT oturum süresi geçersiz.");
        return new AccountProfile(client, subject, email, access, refresh, idToken, scope, DateTimeOffset.UtcNow.AddSeconds(expires));
    }
    private static string RequiredString(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(node.GetString()))
            throw new InvalidDataException("ChatGPT yanıtında gerekli bağlantı bilgisi eksik.");
        return node.GetString()!;
    }
    private static bool HasUsagePermission(string scopes) => scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("chatgpt.tokens.use.direct");
    private static bool FixedEqual(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    private static string RandomValue(int bytes = 32) => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));
    private void SaveProfile(AccountProfile profile) { WriteProtected("account.bin", profile); _profile = profile; }
    private T? ReadProtected<T>(string name)
    {
        var path = Path.Combine(_directory, name);
        if (!File.Exists(path)) return default;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<T>(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void WriteProtected<T>(string name, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        try { AtomicWrite(Path.Combine(_directory, name), ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static async Task<Dictionary<string, string>?> ReadCallbackAsync(Stream stream, string prefix, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var line = await reader.ReadLineAsync(cancellation);
        if (line == null || line.Length > 8192) return null;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "GET" || !parts[1].StartsWith('/')) return null;
        if (!Uri.TryCreate(prefix.TrimEnd('/') + parts[1], UriKind.Absolute, out var uri) || uri.AbsolutePath != "/auth/callback") return null;
        var headerSize = line.Length;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellation)))
        { headerSize += line.Length; if (headerSize > 32768) return null; }
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var values = pair.Split('=', 2);
            if (!query.TryAdd(WebUtility.UrlDecode(values[0]), values.Length == 2 ? WebUtility.UrlDecode(values[1]) : "")) return null;
        }
        return query;
    }
    private static async Task BrowserReply(Stream stream, int status, string message, CancellationToken cancellation)
    {
        var body = Encoding.UTF8.GetBytes("<!doctype html><html lang=\"tr\"><meta charset=\"utf-8\"><title>im too lazy</title><body><h2>im too lazy</h2><p>" + WebUtility.HtmlEncode(message) + "</p></body></html>");
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellation);
        await stream.WriteAsync(body, cancellation);
        await stream.FlushAsync(cancellation);
    }
    public void Dispose() => _http.Dispose();
    private sealed record Registration(string ClientId, string? Subject);
    private sealed record AccountProfile(string ClientId, string Subject, string Email, string AccessToken,
        string? RefreshToken, string IdToken, string Scopes, DateTimeOffset ExpiresAt);
}
