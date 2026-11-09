using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VmixSubtitleController.Output;

public sealed record ObsInput(string Name, string Kind, string Id);
public sealed record ObsSceneItem(string Name, bool Enabled, int Id);
public sealed record ObsMediaStatus(string State, double? PositionMilliseconds, double? DurationMilliseconds);

/// <summary>Small OBS WebSocket v5 client for local production control.</summary>
public sealed class ObsWebSocketClient : IAsyncDisposable
{
    private const int MaximumMessageBytes = 1_000_000;
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _receiveTask;
    private bool _disposed;

    private ObsWebSocketClient() => _socket.Options.AddSubProtocol("obswebsocket.json");

    public bool IsConnected => !_disposed && _socket.State == WebSocketState.Open;

    public static async Task<ObsWebSocketClient> ConnectAsync(Uri endpoint, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme is not ("ws" or "wss") || string.IsNullOrWhiteSpace(endpoint.Host))
            throw new ArgumentException("OBS adresi ws:// veya wss:// biçiminde olmalıdır.", nameof(endpoint));

        var client = new ObsWebSocketClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await client._socket.ConnectAsync(endpoint, timeout.Token);

            using var hello = await client.ReceiveDocumentAsync(timeout.Token);
            var root = hello.RootElement;
            if (GetInt(root, "op") != 0 || !root.TryGetProperty("d", out var helloData))
                throw new InvalidDataException("OBS beklenen WebSocket karşılama mesajını göndermedi.");

            var identifyData = new Dictionary<string, object?> { ["rpcVersion"] = 1 };
            if (helloData.TryGetProperty("authentication", out var authentication))
            {
                if (string.IsNullOrEmpty(password))
                    throw new InvalidOperationException("OBS bağlantısı parola istiyor. OBS WebSocket parolasını girin.");

                var salt = authentication.GetProperty("salt").GetString() ?? "";
                var challenge = authentication.GetProperty("challenge").GetString() ?? "";
                identifyData["authentication"] = CreateAuthentication(password, salt, challenge);
            }

            await client.SendJsonAsync(new { op = 1, d = identifyData }, timeout.Token);
            using var identified = await client.ReceiveDocumentAsync(timeout.Token);
            if (GetInt(identified.RootElement, "op") != 2)
            {
                var detail = identified.RootElement.TryGetProperty("d", out var data) ? data.ToString() : "";
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                    ? "OBS bağlantısı doğrulanamadı. OBS adresini ve parolayı kontrol edin."
                    : "OBS bağlantısı doğrulanamadı: " + detail);
            }

            client._receiveTask = client.ReceiveLoopAsync(client._lifetime.Token);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ObsInput>> GetInputsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await RequestAsync("GetInputList", cancellationToken: cancellationToken);
        if (!response.RootElement.TryGetProperty("inputs", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<ObsInput>();

        return items.EnumerateArray()
            .Select(item => new ObsInput(
                GetString(item, "inputName"),
                GetString(item, "inputKind"),
                GetString(item, "inputUuid")))
            .Where(input => input.Name.Length > 0)
            .ToArray();
    }

    public async Task<string> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default)
    {
        using var response = await RequestAsync("GetCurrentProgramScene", cancellationToken: cancellationToken);
        return GetString(response.RootElement, "currentProgramSceneName");
    }

    public async Task<IReadOnlyList<ObsSceneItem>> GetSceneItemsAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        using var response = await RequestAsync("GetSceneItemList", new { sceneName }, cancellationToken);
        if (!response.RootElement.TryGetProperty("sceneItems", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<ObsSceneItem>();

        return items.EnumerateArray()
            .Select(item => new ObsSceneItem(
                GetString(item, "sourceName"),
                !item.TryGetProperty("sceneItemEnabled", out var enabled) || enabled.GetBoolean(),
                GetInt(item, "sceneItemId")))
            .Where(item => item.Name.Length > 0)
            .ToArray();
    }

    public async Task<ObsMediaStatus> GetMediaStatusAsync(string inputName, CancellationToken cancellationToken = default)
    {
        using var response = await RequestAsync("GetMediaInputStatus", new { inputName }, cancellationToken);
        return new ObsMediaStatus(
            GetString(response.RootElement, "mediaState"),
            GetNullableNumber(response.RootElement, "mediaCursor"),
            GetNullableNumber(response.RootElement, "mediaDuration"));
    }

    public async Task SetSceneItemEnabledAsync(string sceneName, int sceneItemId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var response = await RequestAsync("SetSceneItemEnabled", new { sceneName, sceneItemId, sceneItemEnabled = enabled }, cancellationToken);
    }

    public async Task RestartMediaInputAsync(string inputName, CancellationToken cancellationToken = default)
    {
        using var response = await RequestAsync("TriggerMediaInputAction", new
        {
            inputName,
            mediaAction = "OBS_WEBSOCKET_MEDIA_INPUT_ACTION_RESTART"
        }, cancellationToken);
    }

    public async Task PauseMediaInputAsync(string inputName,CancellationToken cancellationToken=default){using var response=await RequestAsync("TriggerMediaInputAction",new {inputName,mediaAction="OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PAUSE"},cancellationToken);}
    public async Task RewindMediaAsync(string inputName,CancellationToken cancellationToken=default)
    {
        using var rewind=await RequestAsync("SetMediaInputCursor",new {inputName,mediaCursor=0},cancellationToken);
    }
    public async Task PlayMediaInputAsync(string inputName,CancellationToken cancellationToken=default)
    {
        using var play=await RequestAsync("TriggerMediaInputAction",new {inputName,mediaAction="OBS_WEBSOCKET_MEDIA_INPUT_ACTION_PLAY"},cancellationToken);
    }
    public async Task SetTextAsync(string inputName, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputName)) throw new ArgumentException("OBS metin kaynağını seçin.", nameof(inputName));
        using var _ = await RequestAsync("SetInputSettings", new
        {
            inputName,
            inputSettings = new { text },
            overlay = true
        }, cancellationToken);
    }

    private async Task<JsonDocument> RequestAsync(string requestType, object? requestData = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsConnected) throw new InvalidOperationException("OBS bağlantısı kapalı.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("OBS isteği oluşturulamadı.");
        try
        {
            await SendJsonAsync(new
            {
                op = 6,
                d = new { requestType, requestId = id, requestData }
            }, timeout.Token);
            var response = await completion.Task.WaitAsync(timeout.Token);
            if (response.TryGetProperty("requestStatus", out var status) &&
                status.TryGetProperty("result", out var success) && !success.GetBoolean())
            {
                var message = status.TryGetProperty("comment", out var comment) ? comment.GetString() : null;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? $"OBS isteği başarısız oldu: {requestType}"
                    : $"OBS isteği başarısız oldu: {message}");
            }

            return response.TryGetProperty("responseData", out var data)
                ? JsonDocument.Parse(data.GetRawText())
                : JsonDocument.Parse("{}");
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var message = await ReceiveDocumentAsync(cancellationToken);
                var root = message.RootElement;
                if (GetInt(root, "op") != 7 || !root.TryGetProperty("d", out var data) ||
                    !data.TryGetProperty("requestId", out var requestId))
                    continue;

                if (_pending.TryGetValue(requestId.GetString() ?? "", out var completion))
                    completion.TrySetResult(data.Clone());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            foreach (var pending in _pending.Values) pending.TrySetException(ex);
        }
    }

    private async Task SendJsonAsync(object value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await _sendGate.WaitAsync(cancellationToken);
        try { await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken); }
        finally { _sendGate.Release(); }
    }

    private async Task<JsonDocument> ReceiveDocumentAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(chunk, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("OBS WebSocket bağlantısı kapandı.");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("OBS WebSocket JSON mesajı bekleniyordu.");
            buffer.Write(chunk, 0, result.Count);
            if (buffer.Length > MaximumMessageBytes)
                throw new InvalidDataException("OBS WebSocket yanıtı beklenenden büyük.");
        } while (!result.EndOfMessage);

        return JsonDocument.Parse(buffer.ToArray());
    }

    private static string CreateAuthentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static int GetInt(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.TryGetInt32(out var result) ? result : -1;

    private static string GetString(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";

    private static double? GetNullableNumber(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var result) ? result : null;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        if (_socket.State == WebSocketState.Open)
        {
            try { await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); }
            catch { _socket.Abort(); }
        }
        if (_receiveTask != null)
        {
            try { await _receiveTask; }
            catch { }
        }
        foreach (var pending in _pending.Values)
            pending.TrySetException(new ObjectDisposedException(nameof(ObsWebSocketClient)));
        _socket.Dispose();
        _sendGate.Dispose();
        _lifetime.Dispose();
    }
}
