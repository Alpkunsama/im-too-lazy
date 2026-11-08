using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace VmixSubtitleController;

public partial class MainWindow
{
    private Task<bool>? _chatGptConnectTask;
    private bool _updatingChatGptModels;
    private Task? _modelsRefreshTask;
    private int _accountRevision;

    private void UpdateChatGptStatus()
    {
        if (_closed) return;
        TargetLanguageText.Text = "Hedef: " + _preferences.TargetLanguage + " • Ayarlar'dan değiştir";
        var english = _preferences.InterfaceLanguage == "en";
        ChatGptStatusText.Text = _chatGptAccount.CanTranslate ? _chatGptAccount.DisplayName + " • Using ChatGPT plan" :
            _chatGptAccount.HasProfile
                ? (english ? "ChatGPT connected • Plan permission required" : "ChatGPT bağlı • Plan kullanım izni gerekiyor")
                : (english ? "ChatGPT not connected • You can sign in when loading an SRT" : "ChatGPT bağlı değil • SRT yüklerken giriş yapabilirsiniz");
    }

    private async Task<bool> EnsureChatGptConnectedAsync()
    {
        if (_closed) return false;
        if (_chatGptAccount.CanTranslate)
        {
            if (ChatGptModelBox.SelectedItem == null) await RefreshChatGptModelsAsync();
            if (ChatGptModelBox.SelectedItem != null) return true;
            return false;
        }
        if (_chatGptConnectTask != null) return await _chatGptConnectTask;
        _chatGptConnectTask = ConnectChatGptCoreAsync();
        try { return await _chatGptConnectTask; }
        finally { _chatGptConnectTask = null; }
    }

    private async Task<bool> ConnectChatGptCoreAsync()
    {
        _accountRevision++;
        foreach (var slot in _slots) slot.TranslationCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _signInCancellation = cancellation;
        ChatGptConnectButton.Content = "Girişi iptal et";
        ChatGptStatusText.Text = "Uygulamaya ait bağımsız gizli pencerede ChatGPT girişini tamamlayın…";
        try
        {
            await _chatGptAccount.SignInAsync(cancellation.Token);
            if (_closed) return false;
            UpdateChatGptStatus();
            if (!_chatGptAccount.CanTranslate)
                throw new InvalidOperationException("ChatGPT girişiniz tamamlandı fakat bu uygulama için ChatGPT plan kullanımına izin verilmedi.");
            _startupGate = false;
            SendWebState(true);
            PostWeb(new { type = "login-success" });
            if (!_chatGptAccount.WelcomeShown)
            {
                _chatGptAccount.MarkWelcomeShown();
            }
            await RefreshChatGptModelsAsync();
            return ChatGptModelBox.SelectedItem != null;
        }
        catch (OperationCanceledException)
        { if (!_closed) ChatGptStatusText.Text = "ChatGPT girişi iptal edildi veya zaman aşımına uğradı."; return false; }
        catch (Exception ex)
        {
            if (!_closed) { ChatGptStatusText.Text = "ChatGPT bağlantısı tamamlanamadı"; StudioDialog.Show(this, ex.Message, "ChatGPT bağlantısı", MessageBoxButton.OK, MessageBoxImage.Warning); }
            return false;
        }
        finally
        {
            _signInCancellation = null;
            if (!_closed) ChatGptConnectButton.Content = "ChatGPT hesabını bağla";
        }
    }

    private async void ChatGptConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_signInCancellation != null) { _signInCancellation.Cancel(); return; }
        if (_chatGptConnectTask != null) return;
        _chatGptConnectTask = ConnectChatGptCoreAsync();
        try { await _chatGptConnectTask; }
        finally { _chatGptConnectTask = null; }
    }

    private async Task RefreshChatGptModelsAsync()
    {
        if (_modelsRefreshTask != null) { await _modelsRefreshTask; return; }
        _modelsRefreshTask = RefreshChatGptModelsCoreAsync();
        try { await _modelsRefreshTask; }
        finally { _modelsRefreshTask = null; }
    }

    private async Task RefreshChatGptModelsCoreAsync()
    {
        if (_closed || !_chatGptAccount.CanTranslate) return;
        var revision = _accountRevision;
        try
        {
            var models = await _translator.ListModelsAsync(_lifetime.Token);
            if (_closed || revision != _accountRevision) return;
            var selected = (ChatGptModelBox.SelectedItem as ChatGptModel)?.Slug ?? _preferences.ModelSlug;
            _updatingChatGptModels = true;
            try
            {
                ChatGptModelBox.ItemsSource = models;
                ChatGptModelBox.SelectedItem = models.FirstOrDefault(m => m.Slug == selected) ?? models.FirstOrDefault();
            }
            finally { _updatingChatGptModels = false; }
            UpdateChatGptStatus();
            if (models.Count == 0) ChatGptStatusText.Text = "ChatGPT bağlı; bu hesap için kullanılabilir model bulunamadı.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) ChatGptStatusText.Text = ex.Message; }
    }
    private async void ChatGptModels_Click(object sender, RoutedEventArgs e)
    {
        if (!_chatGptAccount.CanTranslate) { ChatGptStatusText.Text = "Model listesini almak için önce ChatGPT hesabını bağlayın."; return; }
        await RefreshChatGptModelsAsync();
    }
    private void ChatGptModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingChatGptModels || ChatGptModelBox.SelectedItem is not ChatGptModel model) return;
        _preferences = _preferences with { ModelSlug = model.Slug };
        try { _preferences.Save(); }
        catch { ChatGptStatusText.Text = "Model seçimi bu oturumda geçerli; ayarlara kaydedilemedi."; }
    }
    private void ChatGptUsage_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true }); }
        catch (Exception ex) { StudioDialog.Show(this, ex.Message, "Kullanımı yönet"); }
    }
    private async void ChatGptLogout_Click(object sender, RoutedEventArgs e)
    {
        if (_closed) return;
        _accountRevision++;
        _signInCancellation?.Cancel();
        foreach (var slot in _slots) slot.TranslationCancellation?.Cancel();
        if (_chatGptConnectTask != null) await _chatGptConnectTask;
        try
        {
            var confirmed = await _chatGptAccount.SignOutAsync(_lifetime.Token);
            ChatGptModelBox.ItemsSource = null;
            UpdateChatGptStatus();
            if (!confirmed) StudioDialog.Show(this, "Bu bilgisayardaki oturum kapatıldı; uzaktaki izin iptali doğrulanamadı. ChatGPT ayarlarından uygulama bağlantısını kaldırabilirsiniz.", "ChatGPT çıkışı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) StudioDialog.Show(this, ex.Message, "ChatGPT çıkışı"); }
    }
    private bool _changingAccount;
    private async Task ChangeChatGptAccountAsync(){
        if(_changingAccount)return;_changingAccount=true;SendWebState(true);try{
        _accountRevision++;_signInCancellation?.Cancel();foreach(var slot in _slots)slot.TranslationCancellation?.Cancel();
        if(_chatGptConnectTask!=null)await _chatGptConnectTask;
        await _chatGptAccount.SignOutAsync(_lifetime.Token);_chatGptAccount.ResetRegistration();ChatGptModelBox.ItemsSource=null;UpdateChatGptStatus();
        _chatGptConnectTask=ConnectChatGptCoreAsync();try{await _chatGptConnectTask;}finally{_chatGptConnectTask=null;}
        }finally{_changingAccount=false;SendWebState(true);}
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsLanguageBox.Text = _preferences.TargetLanguage;
        SettingsDarkBox.IsChecked = DarkModeBox.IsChecked;
        SettingsStatusText.Text = "";
        ShowPage("settings");
    }
}
