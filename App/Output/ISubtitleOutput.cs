namespace VmixSubtitleController.Output;

/// <summary>
/// Common contract for destinations that receive the subtitle currently on air.
/// A destination is free to interpret its target key according to its own API.
/// </summary>
public interface ISubtitleOutput
{
    string Id { get; }

    Task<bool> PublishAsync(
        string text,
        string? targetOverride = null,
        CancellationToken cancellationToken = default);
}
