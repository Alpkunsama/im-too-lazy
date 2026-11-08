using libomtnet;

namespace VmixSubtitleController;

public partial class MainWindow
{
    private int _omtFramePostPending;
    private async Task<string[]> RefreshOmtSourcesAsync()
    {
        _omtSources = await Task.Run(() => OMTDiscovery.GetInstance().GetAddresses());
        if (_omtSources.Length == 0)
        {
            await Task.Delay(1200, _lifetime.Token);
            _omtSources = await Task.Run(() => OMTDiscovery.GetInstance().GetAddresses());
        }
        return _omtSources;
    }

    private void StartOmtReceiver(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || !_omtSources.Contains(address, StringComparer.Ordinal))
            throw new InvalidOperationException("Seçilen OMT kaynağı bulunamadı. Kaynak listesini yenileyin.");

        StopOmtReceiver();
        var receiver = new OmtVideoReceiver(address);
        receiver.FrameReady += image =>
        {
            if (Interlocked.Exchange(ref _omtFramePostPending, 1) != 0) return;
            Dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _omtFramePostPending, 0);
                if (!_closed && _page == "broadcast" && ReferenceEquals(_omtReceiver, receiver))
                    PostWeb(new { type = "omt-frame", image });
            });
        };
        _omtReceiver = receiver;
        _omtSource = address;
    }

    private void StopOmtReceiver()
    {
        var receiver = _omtReceiver;
        _omtReceiver = null;
        _omtSource = "";
        if (receiver != null) _ = Task.Run(receiver.Dispose);
    }
}
