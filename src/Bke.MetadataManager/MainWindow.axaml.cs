using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Bke.MetadataManager.ViewModels;

namespace Bke.MetadataManager;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void ChooseFiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import media files", AllowMultiple = true });
        var paths = files.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);
        await _viewModel.ImportPathsAsync(paths);
    }

    private async void ChooseFolders_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Import folders", AllowMultiple = true });
        var paths = folders.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);
        await _viewModel.ImportPathsAsync(paths);
    }
}
