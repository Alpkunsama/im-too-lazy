using System.Diagnostics;
using System.Windows;

namespace VmixSubtitleController;

internal static class AppVersion
{
    public const string Current = "0.0079";
}

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;
    internal string? UpdateInstaller { get; set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(false, "Local\\ImTooLazySubtitleController");
        try { _ownsInstance = _instance.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsInstance = true; }
        if (!_ownsInstance) { MessageBox.Show("im too lazy zaten açık."); Shutdown(); return; }
        base.OnStartup(e);
        new MainWindow().Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
        if (UpdateInstaller is not { } installer) return;
        try { Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Kurucu başlatılamadı. Dosyayı elle açabilirsiniz.\n\n{installer}\n\n{ex.Message}",
                "im too lazy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "im-too-lazy", "startup-error.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
            MessageBox.Show(
                $"im too lazy açılırken bir hata oluştu:\n\n{e.Exception}",
                "im too lazy - Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
