namespace VmixSubtitleController.Output;

/// <summary>Writes a changing subtitle into an existing OBS text input.</summary>
public sealed class ObsSubtitleOutput : ISubtitleOutput
{
    private readonly ObsWebSocketClient _client;
    private readonly Func<string?> _selectedTextInput;

    public string Id => "obs";

    public ObsSubtitleOutput(ObsWebSocketClient client, Func<string?> selectedTextInput)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _selectedTextInput = selectedTextInput ?? throw new ArgumentNullException(nameof(selectedTextInput));
    }

    public async Task<bool> PublishAsync(string text, string? targetOverride = null, CancellationToken cancellationToken = default)
    {
        var target = targetOverride ?? _selectedTextInput();
        if (string.IsNullOrWhiteSpace(target) || !_client.IsConnected) return false;
        await _client.SetTextAsync(target, text, cancellationToken);
        return true;
    }
}
