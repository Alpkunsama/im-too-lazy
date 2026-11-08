using System.Net.Http;

namespace VmixSubtitleController.Output;

/// <summary>
/// Sends subtitle text to the selected GT or XAML title field through the HTTP API.
/// Discovery and input selection remain owned by the existing vMix connection flow.
/// </summary>
public sealed class VmixSubtitleOutput : ISubtitleOutput
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiBase;
    private readonly Func<string?> _selectedTitleKey;
    private readonly Func<string, string> _textField;
    private readonly Func<string, string?> _visibilityField;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public string Id => "vmix";

    public VmixSubtitleOutput(HttpClient http, Func<string?> apiBase, Func<string?> selectedTitleKey,
        Func<string, string>? textField = null, Func<string, string?>? visibilityField = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
        _selectedTitleKey = selectedTitleKey ?? throw new ArgumentNullException(nameof(selectedTitleKey));
        _textField = textField ?? (_ => "Message.Text");
        _visibilityField = visibilityField ?? (_ => null);
    }

    public async Task<bool> PublishAsync(
        string text,
        string? targetOverride = null,
        CancellationToken cancellationToken = default)
    {
        var api = _apiBase()?.Trim().TrimEnd('/');
        var target = targetOverride ?? _selectedTitleKey();
        if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(target))
            return false;

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            var field = _textField(target);
            var visibilityField = _visibilityField(target);
            var hasText = !string.IsNullOrWhiteSpace(text);
            // Hide the entire subtitle panel before clearing the text. Never leave
            // padding visible while vMix normalizes an empty field to whitespace.
            if (visibilityField != null && !hasText)
                await SetTextAsync(api, target, visibilityField, "Collapsed", cancellationToken);
            await SetTextAsync(api, target, field, hasText ? text : "", cancellationToken);
            if (visibilityField != null && hasText)
                await SetTextAsync(api, target, visibilityField, "Visible", cancellationToken);
            return true;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task SetTextAsync(string api, string target, string field, string value, CancellationToken token)
    {
        var url = $"{api}/?Function=SetText&Input={Uri.EscapeDataString(target)}&SelectedName={Uri.EscapeDataString(field)}&Value={Uri.EscapeDataString(value)}";
        using var response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
    }
}
