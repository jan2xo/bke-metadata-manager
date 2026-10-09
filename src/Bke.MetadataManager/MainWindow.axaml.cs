using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Bke.MetadataManager.Models;
using Bke.MetadataManager.ViewModels;

namespace Bke.MetadataManager;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Opened += async (_, _) =>
        {
            try { await _viewModel.InitializeAsync(); }
            catch (Exception ex) { _viewModel.ReportUiError($"Catalog startup failed: {ex.Message}"); }
        };
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

    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_Drop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        var paths = e.Data.GetFiles()
            .Select(item => item.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!);
        await _viewModel.ImportPathsAsync(paths);
        e.Handled = true;
    }

    private async void Assets_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is ImportedAsset asset)
            await _viewModel.ReadMetadataAsync(asset);
        else
            await _viewModel.ReadMetadataAsync(null);
    }
}
