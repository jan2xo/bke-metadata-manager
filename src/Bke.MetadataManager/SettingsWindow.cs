using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager;

public sealed class SettingsWindow : Window
{
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public SettingsWindow(AppSettings settings, SettingsStore store)
    {
        Title = "Settings · BKE Media Organizer";
        Width = 670;
        Height = 418;
        MinWidth = 540;
        MinHeight = 375;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#202A41");
        Foreground = Brush("#F9FAFC");
        FontFamily = FontFamily.Parse("Inter");

        var title = new TextBlock
        {
            Text = "Settings", FontSize = 26,
            FontWeight = FontWeight.SemiBold, Foreground = Brush("#F8F9FC")
        };
        var subtitle = new TextBlock
        {
            Text = "Organize footage your way.",
            FontSize = 13, Foreground = Brush("#B9C3D6")
        };
        var current = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(settings.DestinationFolder)
                ? "No destination configured" : settings.DestinationFolder,
            Foreground = Brush("#F1F4FA"), TextWrapping = TextWrapping.Wrap,
            FontSize = 14, FontWeight = FontWeight.Medium
        };
        var feedback = new TextBlock
        {
            Foreground = Brush("#D9C18F"), FontSize = 12,
            Text = "Files will be moved to Destination / Event / Category.",
            TextWrapping = TextWrapping.Wrap
        };
        var choose = new Button
        {
            Content = "Choose destination…", Background = Brush("#40FFFFFF"),
            BorderBrush = Brush("#79D3D9E6"), BorderThickness = new Thickness(1),
            Foreground = Brush("#FAFAFD"), FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 11),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        choose.Click += async (_, _) =>
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Where should organized footage be moved?",
                    AllowMultiple = false
                });
                var path = folders.FirstOrDefault()?.Path.LocalPath;
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!Directory.Exists(path)) throw new IOException("The folder is inaccessible.");
                settings.DestinationFolder = Path.GetFullPath(path);
                store.Save(settings);
                current.Text = settings.DestinationFolder;
                feedback.Text = "Saved. The original source disappears only after a verified move.";
            }
            catch (Exception ex) { feedback.Text = "Could not save destination: " + ex.Message; }
        };

        var destinationPanel = new Border
        {
            Background = Brush("#27FFFFFF"), BorderBrush = Brush("#45FFFFFF"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17),
            Padding = new Thickness(19, 18),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "DESTINATION FOLDER",
                        FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Brush("#C4CEDF")
                    },
                    current,
                    choose,
                    feedback
                }
            }
        };

        var main = new StackPanel
        {
            Spacing = 18, Margin = new Thickness(28, 26),
            Children =
            {
                title, subtitle, destinationPanel,
                new TextBlock
                {
                    Text = "More preferences will appear here as the app grows.",
                    Foreground = Brush("#9FAAC1"), FontSize = 12
                }
            }
        };
        Content = new Border
        {
            Margin = new Thickness(8),
            CornerRadius = new CornerRadius(22),
            BorderBrush = Brush("#66D2DEF1"), BorderThickness = new Thickness(1),
            Background = Brush("#E3293550"),
            Child = new ScrollViewer { Content = main }
        };
    }
}
