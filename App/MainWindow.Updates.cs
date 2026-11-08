using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace VmixSubtitleController;

public partial class MainWindow
{
    private bool _updateChecked;
    private bool _updateBusy;
    private string _updateStatus = "";
    private ReleaseUpdate? _availableUpdate;

    private async Task CheckForUpdateAsync(bool manual = false, ReleaseUpdate? knownRelease = null)
    {
        if (_updateBusy || (!manual && _updateChecked)) return;
        _updateChecked = true;
        _updateBusy = true;
        if (knownRelease == null) SetUpdateStatus("Güncellemeler denetleniyor…");
        var feedUrl = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(metadata => metadata.Key == "UpdateFeedUrl")?.Value;
        var installing = false;
        try
        {
            var update = knownRelease ?? (string.IsNullOrWhiteSpace(feedUrl)
                ? await ReleaseUpdate.CheckGitHubAsync(AppVersion.Current, _lifetime.Token)
                : await ReleaseUpdate.CheckAsync(feedUrl, AppVersion.Current, _lifetime.Token));
            if (_closed) return;
            _availableUpdate = update;
            if (update == null)
            {
                SetUpdateStatus($"Güncelleme bulunamadı. Yüklü sürüm {AppVersion.Current}.");
                return;
            }

            var sections = new List<string> { $"Yüklü sürüm {AppVersion.Current}  →  Yeni sürüm {update.Version}" };
            if (update.Added.Length > 0) sections.Add("Eklenenler\n• " + string.Join("\n• ", update.Added));
            if (update.Fixed.Length > 0) sections.Add("Düzeltilenler\n• " + string.Join("\n• ", update.Fixed));
            if (!string.IsNullOrWhiteSpace(update.Notes)) sections.Add(update.Notes);
            var inApp = update.Sha256 != null;
            sections.Add(inApp
                ? "Çalışmanızı kaydedin. İndirme tamamlanınca uygulama kapanacak ve Windows kurucusu açılacak."
                : "Kurulum dosyasını indirmek için tarayıcıyı açabilirsiniz.");
            SetUpdateStatus($"{update.Version} sürümü mevcut.");
            if (StudioDialog.Show(this, string.Join("\n\n", sections), "Güncelleme var",
                MessageBoxButton.YesNo, yesLabel: inApp ? "İndir ve kur" : "İndirmeyi aç",
                noLabel: "Daha sonra") != MessageBoxResult.Yes) return;

            if (!inApp)
            {
                Process.Start(new ProcessStartInfo(update.Download.AbsoluteUri) { UseShellExecute = true });
                return;
            }

            installing = true;
            SetUpdateStatus($"{update.Version} indiriliyor…");
            var installer = await update.DownloadInstallerAsync(_lifetime.Token);
            if (_closed) { File.Delete(installer); return; }
            ((App)Application.Current).UpdateInstaller = installer;
            Close();
        }
        catch (Exception ex)
        {
            if (_closed) return;
            var message = installing ? "Güncelleme indirilemedi veya doğrulanamadı." : "Güncellemeler denetlenemedi.";
            SetUpdateStatus(message);
            if (installing) StudioDialog.Show(this, $"{message}\n\n{ex.Message}", "Güncelleme");
        }
        finally
        {
            _updateBusy = false;
            if (!_closed) SendWebState(true);
        }
    }

    private void SetUpdateStatus(string status)
    {
        _updateStatus = status;
        SendWebState(true);
    }
}
