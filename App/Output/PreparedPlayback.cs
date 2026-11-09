namespace VmixSubtitleController.Output;

internal enum PreparedMediaState { Waiting, PausedAtStart, NeedsPause, NeedsRewind, NeedsRestart }

internal static class PreparedPlayback
{
    internal static bool IsAtStart(double? position) => position is >= 0 and <= 40;

    internal static async Task ConfirmAsync(
        Func<CancellationToken, Task<PreparedMediaState>> read,
        Func<PreparedMediaState, CancellationToken, Task> correct,
        CancellationToken token,
        TimeSpan? sampleInterval = null)
    {
        var stable = 0;
        var restarted = false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(sampleInterval ?? TimeSpan.FromMilliseconds(100), token);
            var state = await read(token);
            if (state == PreparedMediaState.PausedAtStart)
            {
                if (++stable == 3) return;
                continue;
            }

            stable = 0;
            if (state is PreparedMediaState.NeedsPause or PreparedMediaState.NeedsRewind)
                await correct(state, token);
            else if (state == PreparedMediaState.NeedsRestart && !restarted)
            {
                restarted = true;
                await correct(state, token);
            }
        }
        throw new InvalidOperationException("Video çıkışta başa sarılmış ve duraklatılmış olarak doğrulanamadı.");
    }
}
