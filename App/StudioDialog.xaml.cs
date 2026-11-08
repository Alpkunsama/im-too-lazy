using System.Windows;
using System.Windows.Controls;

namespace VmixSubtitleController;

public partial class StudioDialog : Window
{
    private MessageBoxResult _result;

    private StudioDialog(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon,
        string? yesLabel = null, string? noLabel = null)
    {
        InitializeComponent();
        Title = caption;
        Heading.Text = caption;
        Message.Text = message;
        _result = buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OK => MessageBoxResult.OK,
            _ => MessageBoxResult.Cancel
        };
        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel)
        {
            if (buttons == MessageBoxButton.YesNoCancel) AddAction("İptal", MessageBoxResult.Cancel, cancel: true);
            AddAction(noLabel ?? "Hayır", MessageBoxResult.No, cancel: buttons == MessageBoxButton.YesNo);
            AddAction(yesLabel ?? "Evet", MessageBoxResult.Yes, primary: true);
        }
        else
        {
            if (buttons == MessageBoxButton.OKCancel) AddAction("İptal", MessageBoxResult.Cancel, cancel: true);
            AddAction("Tamam", MessageBoxResult.OK, primary: true, cancel: buttons == MessageBoxButton.OK);
        }
        if (icon is MessageBoxImage.Error or MessageBoxImage.Warning)
            Heading.Foreground = (System.Windows.Media.Brush)FindResource("StudioAccent");
    }

    private void AddAction(string label, MessageBoxResult result, bool primary = false, bool cancel = false)
    {
        var button = new Button
        {
            Content = label, MinWidth = 82, Margin = new Thickness(8, 0, 0, 0),
            IsDefault = primary, IsCancel = cancel,
            Style = (Style)FindResource(primary ? "StudioPrimaryButton" : "StudioButton")
        };
        button.Click += (_, _) => { _result = result; Close(); };
        Actions.Children.Add(button);
    }

    public static MessageBoxResult Show(Window owner, string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
        string? yesLabel = null, string? noLabel = null)
    {
        var dialog = new StudioDialog(message, caption, buttons, icon, yesLabel, noLabel);
        if (owner.IsLoaded) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog._result;
    }
}
