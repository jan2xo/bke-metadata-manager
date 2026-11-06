using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager;

public sealed class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings, SettingsStore store)
    {
        Title = "Settings · BKE Media Organizer";
        Width = 630; Height = 350; MinWidth = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#0B0B0B"));
        Foreground = Brushes.White;
        var yellow = new SolidColorBrush(Color.Parse("#FFD229"));
        var current = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.DestinationFolder) ? "No destination configured" : settings.DestinationFolder,
            Foreground = new SolidColorBrush(Color.Parse("#BBBBBB")), TextWrapping = TextWrapping.Wrap, FontSize = 13
        };
        var feedback = new TextBlock { Foreground = yellow, FontSize = 12, Text = "This folder will contain Event / Category / renamed media files." };
        var choose = new Button
        {
            Content = "CHOOSE DESTINATION FOLDER",
            HorizontalAlignment = HorizontalAlignment.Left, Background = yellow,
            Foreground = Brushes.Black, FontWeight = FontWeight.Bold, Padding = new Thickness(16, 12)
        };
        choose.Click += async (_, _) =>
        {
            try
            {
                var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Where should organized footage be moved?", AllowMultiple = false
                });
                var path = result.FirstOrDefault()?.Path.LocalPath;
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!Directory.Exists(path)) throw new IOException("The selected destination is not accessible.");
                settings.DestinationFolder = Path.GetFullPath(path);
                store.Save(settings);
                current.Text = settings.DestinationFolder;
                feedback.Text = "Saved. Files will be MOVED here, not duplicated.";
            }
            catch (Exception ex) { feedback.Text = "Could not save destination: " + ex.Message; }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(32), Spacing = 16,
            Children =
            {
                new TextBlock { Text = "SETTINGS", Foreground = yellow, FontSize = 24, FontWeight = FontWeight.Bold },
                new Border { Height = 2, Background = yellow },
                new TextBlock { Text = "DESTINATION FOLDER", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeight.Bold },
                current, choose, feedback,
                new TextBlock { Text = "Additional settings can be introduced later without changing the main drop workflow.",
                    Foreground = new SolidColorBrush(Color.Parse("#777777")), FontSize = 11, TextWrapping = TextWrapping.Wrap }
            }
        };
    }
}
