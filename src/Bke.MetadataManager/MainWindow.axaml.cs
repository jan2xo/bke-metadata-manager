using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager;

public partial class MainWindow : Window
{
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFD229"));
    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#635623"));
    private static readonly IBrush Tile = new SolidColorBrush(Color.Parse("#171717"));
    private static readonly IBrush Hover = new SolidColorBrush(Color.Parse("#302909"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#999999"));
    private readonly SettingsStore _store = new();
    private readonly MediaOrganizer _organizer = new();
    private readonly Dictionary<string, int> _totals = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private bool _busy;
    private int _moved;
    private bool _editingCategory;

    public MainWindow()
    {
        InitializeComponent();
        try { _settings = _store.Load(); }
        catch (Exception ex)
        {
            _settings = new AppSettings();
            StatusText.Text = "Settings could not load: " + ex.Message;
        }
        RenderTiles();
        EventBox.Focus();
    }

    private void EventBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (StatusText is null) return;
        if (string.IsNullOrWhiteSpace(EventBox.Text))
            StatusText.Text = "Type an event name before dropping files.";
    }

    private void RenderTiles()
    {
        TilesPanel.Children.Clear();
        foreach (var category in _settings.Categories)
            TilesPanel.Children.Add(MakeDropTile(category));
        TilesPanel.Children.Add(MakeAddTile());
    }

    private Border MakeDropTile(string category)
    {
        var n = _totals.GetValueOrDefault(category);
        var card = new Border
        {
            Width = 262, Height = 157, Margin = new Thickness(0, 0, 14, 14),
            Background = Tile, BorderBrush = Idle, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(19),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "↓", FontSize = 24, Foreground = Yellow, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = category.ToUpperInvariant(), FontSize = 16, Foreground = Brushes.White,
                        FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = n > 0 ? $"{n} moved  ·  Drop more" : "DROP FILES HERE", Foreground = Gray, FontSize = 11 }
                }
            }
        };
        DragDrop.SetAllowDrop(card, true);
        card.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            if (CanDrop(e)) { card.Background = Hover; card.BorderBrush = Yellow; card.BorderThickness = new Thickness(2.5); }
            e.Handled = true;
        });
        card.AddHandler(DragDrop.DragLeaveEvent, (_, e) => { ResetTile(card); e.Handled = true; });
        card.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            // The OS drag source must NOT delete anything for us; our verified async mover owns source removal.
            e.DragEffects = CanDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        card.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            ResetTile(card);
            // The OS drag source must NOT delete anything for us; our verified async mover owns source removal.
            e.DragEffects = CanDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (e.DragEffects != DragDropEffects.Copy) return;
            var paths = e.Data.GetFiles()?.Select(f => f.Path.LocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!).ToArray() ?? [];
            _ = ProcessDropAsync(paths, category);
        });
        return card;
    }

    private bool CanDrop(DragEventArgs e) =>
        !_busy && !string.IsNullOrWhiteSpace(EventBox.Text) &&
        !string.IsNullOrWhiteSpace(_settings.DestinationFolder) &&
        Directory.Exists(_settings.DestinationFolder) && e.Data.Contains(DataFormats.Files);

    private static void ResetTile(Border card)
    {
        card.Background = Tile;
        card.BorderBrush = Idle;
        card.BorderThickness = new Thickness(1.5);
    }

    private async Task ProcessDropAsync(string[] paths, string category)
    {
        if (_busy || paths.Length == 0) return;
        _busy = true;
        var eventName = EventBox.Text!.Trim();
        var destination = _settings.DestinationFolder;
        var failures = new List<string>();
        var warnings = new List<string>();
        var completed = 0;
        try
        {
            foreach (var path in paths)
            {
                StatusText.Text = $"Moving {Path.GetFileName(path)} → {category}...";
                try
                {
                    var result = await _organizer.MoveAsync(path, destination, eventName, category);
                    completed++;
                    _moved++;
                    if (result.Warning is not null) warnings.Add(result.Warning);
                    _totals[category] = _totals.GetValueOrDefault(category) + 1;
                }
                catch (Exception ex) { failures.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
                CounterText.Text = $"{_moved} MOVED";
            }
        }
        finally { _busy = false; RenderTiles(); }
        StatusText.Text = failures.Count > 0
            ? $"{completed}/{paths.Length} moved. {failures.Count} rejected (source retained). {string.Join("  |  ", failures.Take(2))}"
            : warnings.Count > 0
                ? $"{completed} moved. Warning: {string.Join("  |  ", warnings.Take(1))}"
                : $"{completed} file(s) moved and renamed to {eventName}/{category}.";
    }

    private Control MakeAddTile()
    {
        if (_editingCategory)
        {
            var input = new TextBox
            {
                Watermark = "Category name", Width = 220, FontSize = 14,
                Background = Tile, Foreground = Brushes.White,
                BorderBrush = Yellow, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var save = new Button
            {
                Content = "ADD CATEGORY ↵", Background = Yellow, Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Stretch, FontWeight = FontWeight.Bold
            };
            void Commit()
            {
                try
                {
                    var value = PathNames.Segment(input.Text);
                    if (_settings.Categories.Contains(value, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("That category already exists.");
                    _settings.Categories.Add(value);
                    _store.Save(_settings);
                    _editingCategory = false;
                    RenderTiles();
                    StatusText.Text = $"Category {value} added. Drop files directly onto its tile.";
                }
                catch (Exception ex) { StatusText.Text = ex.Message; }
            }
            save.Click += (_, _) => Commit();
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
                if (e.Key == Key.Escape) { _editingCategory = false; RenderTiles(); }
            };
            Dispatcher.UIThread.Post(() => input.Focus());
            return new Border
            {
                Width = 262, Height = 157, Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 14, 14), CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1.5), BorderBrush = Yellow, Background = Tile,
                Child = new StackPanel { Spacing = 4, Children = { input, save } }
            };
        }
        var plus = new Button
        {
            Width = 262, Height = 157, Margin = new Thickness(0, 0, 14, 14),
            Background = new SolidColorBrush(Color.Parse("#111111")),
            Foreground = Yellow, BorderBrush = Idle,
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12),
            Content = new StackPanel
            {
                Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "+", FontSize = 35, Foreground = Yellow,
                        HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "ADD CATEGORY", FontSize = 12, FontWeight = FontWeight.Bold,
                        Foreground = Yellow, HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
        };
        plus.Click += (_, _) => { _editingCategory = true; RenderTiles(); };
        return plus;
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings, _store);
        await dialog.ShowDialog(this);
        RenderTiles();
        StatusText.Text = Directory.Exists(_settings.DestinationFolder)
            ? "Destination: " + _settings.DestinationFolder
            : "Choose a destination folder in Settings before dropping files.";
    }
}
