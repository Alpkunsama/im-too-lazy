using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace VmixSubtitleController;

public partial class MainWindow
{
    private bool _updateChecked;

    private async Task CheckForUpdateAsync()
    {
        if (_updateChecked) return;
        _updateChecked = true;
        var feedUrl = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(metadata => metadata.Key == "UpdateFeedUrl")?.Value;
        var update = await ReleaseUpdate.CheckAsync(feedUrl, AppVersion.Current, _lifetime.Token);
        if (_closed || update == null) return;

        var sections = new List<string> { $"Yüklü sürüm {AppVersion.Current}  →  Yeni sürüm {update.Version}" };
        if (update.Added.Length > 0) sections.Add("Eklenenler\n• " + string.Join("\n• ", update.Added));
        if (update.Fixed.Length > 0) sections.Add("Düzeltilenler\n• " + string.Join("\n• ", update.Fixed));
        sections.Add("Kurulum dosyasını indirmek için tarayıcıyı açabilirsiniz.");
        var choice = StudioDialog.Show(this, string.Join("\n\n", sections), "Güncelleme var",
            MessageBoxButton.YesNo, yesLabel: "İndirmeyi aç", noLabel: "Daha sonra");
        if (choice != MessageBoxResult.Yes) return;

        try { Process.Start(new ProcessStartInfo(update.Download.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { StudioDialog.Show(this, ex.Message, "Güncelleme bağlantısı"); }
    }
}
