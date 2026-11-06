using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager;

public partial class MainWindow : Window
{
    private static readonly IBrush Gold = Brush("#FFE0A3");
    private static readonly IBrush White = Brush("#F8F9FD");
    private static readonly IBrush Muted = Brush("#B7C0D0");
    private static readonly IBrush TileIdle = Brush("#2AFFFFFF");
    private static readonly IBrush TileHover = Brush("#42F5D298");
    private static readonly IBrush BorderIdle = Brush("#B4E8C88E");
    private static readonly IBrush BorderHover = Brush("#FFFFC971");
    private static readonly IBrush AddIdle = Brush("#18FFFFFF");
    private readonly SettingsStore _store = new();
    private readonly MediaOrganizer _organizer = new();
    private readonly Dictionary<string, int> _totals = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private bool _busy;
    private int _moved;
    private bool _editingCategory;

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

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
        RefreshEventSummary();
        Opened += (_, _) => EventBox.Focus();
    }

    private void EventBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (FilenamePreviewText is null || DestinationText is null) return;
        RefreshEventSummary();
    }

    private void ClearEvent_Click(object? sender, RoutedEventArgs e) => EventBox.Text = string.Empty;

    private void RefreshEventSummary()
    {
        var eventInput = EventBox.Text?.Trim();
        var destination = _settings.DestinationFolder;
        var eventDisplay = "EVENT";
        if (!string.IsNullOrWhiteSpace(eventInput))
        {
            try { eventDisplay = PathNames.Segment(eventInput); }
            catch (ArgumentException) { eventDisplay = "INVALID-EVENT"; }
        }
        FilenamePreviewText.Text = $"{DateTime.Today:yyyy-MM-dd}-C0123-{eventDisplay}-Drone.MP4";
        if (string.IsNullOrWhiteSpace(destination))
            DestinationText.Text = "Choose destination in Settings";
        else if (string.IsNullOrWhiteSpace(eventInput) || eventDisplay == "INVALID-EVENT")
            DestinationText.Text = destination;
        else
            DestinationText.Text = System.IO.Path.Combine(destination, eventDisplay);

        if (string.IsNullOrWhiteSpace(eventInput) && _moved == 0)
            StatusText.Text = "Name an event, choose a destination, then drop footage.";
        else if (!Directory.Exists(destination) && _moved == 0)
            StatusText.Text = "Choose a destination folder in Settings to enable file drops.";
        else if (_moved == 0)
            StatusText.Text = "Drop files onto a category. Each file will be moved and renamed automatically.";
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
        var count = _totals.GetValueOrDefault(category);
        var contents = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 13,
            Children =
            {
                CategoryIcon(category),
                new TextBlock
                {
                    Text = category, FontSize = 19, FontWeight = FontWeight.SemiBold,
                    Foreground = White, HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 206
                },
                new TextBlock
                {
                    Text = count > 0 ? $"{count} moved · Drop more" : "Drop files here",
                    FontSize = 12, Foreground = Muted,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
        var card = new Border
        {
            Width = 244, Height = 217, Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(14), CornerRadius = new CornerRadius(22),
            Background = TileIdle, BorderBrush = BorderIdle,
            BorderThickness = new Thickness(1.25), Child = contents
        };
        DragDrop.SetAllowDrop(card, true);
        card.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            if (CanDrop(e))
            {
                card.Background = TileHover;
                card.BorderBrush = BorderHover;
                card.BorderThickness = new Thickness(2.5);
            }
            e.Handled = true;
        });
        card.AddHandler(DragDrop.DragLeaveEvent, (_, e) =>
        {
            ResetTile(card);
            e.Handled = true;
        });
        card.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            // Report Copy to Finder so Finder never deletes the source on our behalf.
            // Our file mover independently verifies then removes the source.
            e.DragEffects = CanDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        card.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            ResetTile(card);
            e.DragEffects = CanDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            if (e.DragEffects != DragDropEffects.Copy) return;
            var paths = e.Data.GetFiles()?.Select(file => file.Path.LocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!).ToArray() ?? [];
            _ = ProcessDropAsync(paths, category);
        });
        return card;
    }

    private static Control CategoryIcon(string category)
    {
        var data = category.ToLowerInvariant() switch
        {
            "establishing" => "M3 5 L16 5 Q18 5 18 7 L18 17 Q18 19 16 19 L3 19 Q2 19 2 17 L2 7 Q2 5 3 5 Z M18 9 L23 6 L23 18 L18 15",
            "detail" => "M3 4 L21 4 L21 20 L3 20 Z M3 17 L9 11 L13 15 L17 12 L21 16 M8 8 L10 8 L10 10 L8 10 Z",
            "drone" => "M2 6 L8 6 M16 6 L22 6 M5 6 L5 11 L19 11 L19 6 M9 11 L9 15 L15 15 L15 11 M9 15 L8 19 M15 15 L16 19 M11 14 L13 14",
            _ => "M2 7 L2 5 Q2 4 4 4 L10 4 L12 6 L21 6 Q22 6 22 8 L22 19 Q22 20 20 20 L4 20 Q2 20 2 18 Z"
        };
        var shape = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = Gold, Fill = Brushes.Transparent,
            StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        var canvas = new Canvas { Width = 24, Height = 24, Children = { shape } };
        return new Viewbox
        {
            Width = 50, Height = 50,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = canvas
        };
    }

    private bool CanDrop(DragEventArgs e) =>
        !_busy && !string.IsNullOrWhiteSpace(EventBox.Text) &&
        !string.IsNullOrWhiteSpace(_settings.DestinationFolder) &&
        Directory.Exists(_settings.DestinationFolder) && e.Data.Contains(DataFormats.Files);

    private static void ResetTile(Border card)
    {
        card.Background = TileIdle;
        card.BorderBrush = BorderIdle;
        card.BorderThickness = new Thickness(1.25);
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
                StatusText.Text = $"Moving {System.IO.Path.GetFileName(path)} to {category}…";
                try
                {
                    var result = await _organizer.MoveAsync(path, destination, eventName, category);
                    completed++;
                    _moved++;
                    if (result.Warning is not null) warnings.Add(result.Warning);
                    _totals[category] = _totals.GetValueOrDefault(category) + 1;
                }
                catch (Exception ex)
                {
                    failures.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}");
                }
                CounterText.Text = $"{_moved} {(_moved == 1 ? "file" : "files")} moved and ready";
            }
        }
        finally
        {
            _busy = false;
            RenderTiles();
        }
        StatusText.Text = failures.Count > 0
            ? $"{completed}/{paths.Length} moved · {failures.Count} rejected (source retained). {string.Join(" | ", failures.Take(2))}"
            : warnings.Count > 0
                ? $"{completed} moved · Warning: {string.Join(" | ", warnings.Take(1))}"
                : $"Organized into {category} · Moved and renamed to {PathNames.Segment(eventName)}.";
    }

    private Control MakeAddTile()
    {
        if (_editingCategory)
        {
            var input = new TextBox
            {
                Watermark = "Category name", FontSize = 14, Height = 48,
                Background = Brush("#25FFFFFF"), Foreground = White,
                BorderBrush = Gold, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 10)
            };
            var save = new Button
            {
                Content = "Add category", Background = Brush("#E9D2A3"),
                Foreground = Brush("#23283A"), FontWeight = FontWeight.SemiBold,
                CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 9),
                HorizontalAlignment = HorizontalAlignment.Stretch
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
                    StatusText.Text = $"Category {value} added. Drop footage directly onto its tile.";
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
                Width = 244, Height = 217, Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 14, 14),
                CornerRadius = new CornerRadius(22),
                Background = AddIdle, BorderBrush = BorderIdle,
                BorderThickness = new Thickness(1.25),
                Child = new StackPanel
                {
                    Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "New category", Foreground = White,
                            FontSize = 14, FontWeight = FontWeight.SemiBold },
                        input, save
                    }
                }
            };
        }
        var plusCircle = new Border
        {
            Width = 52, Height = 52,
            CornerRadius = new CornerRadius(26),
            BorderBrush = Brush("#83D9E0EC"), BorderThickness = new Thickness(1),
            Background = Brush("#40FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = "+", FontSize = 29, Foreground = White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var plus = new Button
        {
            Width = 244, Height = 217, Margin = new Thickness(0, 0, 14, 14),
            Background = AddIdle, BorderBrush = Brush("#74BFC6D7"),
            Foreground = White, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Content = new StackPanel
            {
                Spacing = 16, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    plusCircle,
                    new TextBlock { Text = "Add Category", FontSize = 16, Foreground = Muted,
                        HorizontalAlignment = HorizontalAlignment.Center }
                }
            }
        };
        plus.Click += (_, _) => { _editingCategory = true; RenderTiles(); };
        return plus;
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        await new SettingsWindow(_settings, _store).ShowDialog(this);
        RenderTiles();
        RefreshEventSummary();
    }

    private void RevealDestination_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var root = _settings.DestinationFolder;
            if (!Directory.Exists(root))
            {
                StatusText.Text = "Set a destination folder in Settings first.";
                return;
            }
            var eventName = EventBox.Text?.Trim();
            var folder = root;
            if (!string.IsNullOrWhiteSpace(eventName))
            {
                var eventFolder = System.IO.Path.Combine(root, PathNames.Segment(eventName));
                if (Directory.Exists(eventFolder)) folder = eventFolder;
            }
            var executable = OperatingSystem.IsMacOS() ? "open"
                : OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open";
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            start.ArgumentList.Add(folder);
            Process.Start(start);
        }
        catch (Exception ex) { StatusText.Text = "Unable to reveal destination: " + ex.Message; }
    }
}
