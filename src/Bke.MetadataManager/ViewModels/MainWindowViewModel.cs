using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Bke.MetadataManager.Models;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly FileFingerprintService _fingerprints = new();
    private readonly SqliteCatalog _catalog;
    private readonly ExifToolMetadataReader _metadataReader;
    private readonly HashSet<string> _knownPaths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _initialized;
    private string _statusMessage = "Starting catalog…";
    private string _progressMessage = "No import in progress.";
    private int _importProgress;
    private bool _isImporting;
    private ImportedAsset? _selectedAsset;
    private string _captureTimestamp = "—";
    private string _creator = "—";
    private string _description = "—";
    private string _keywords = "—";
    private string _copyright = "—";
    private string _metadataError = "";

    public MainWindowViewModel(string? databasePath = null, string? exifToolPath = null)
    {
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BKE Metadata Manager", "catalog.db");
        _catalog = new SqliteCatalog(databasePath ?? defaultPath);
        _metadataReader = new ExifToolMetadataReader(
            exifToolPath ?? Environment.GetEnvironmentVariable("EXIFTOOL_PATH") ?? "exiftool");
    }

    public ObservableCollection<ImportedAsset> Assets { get; } = new();
    public ObservableCollection<MetadataEntry> MetadataItems { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ProgressMessage { get => _progressMessage; private set => SetProperty(ref _progressMessage, value); }
    public int ImportProgress { get => _importProgress; private set => SetProperty(ref _importProgress, value); }
    public bool IsImporting { get => _isImporting; private set => SetProperty(ref _isImporting, value); }
    public ImportedAsset? SelectedAsset { get => _selectedAsset; set => SetProperty(ref _selectedAsset, value); }
    public string CaptureTimestamp { get => _captureTimestamp; private set => SetProperty(ref _captureTimestamp, value); }
    public string Creator { get => _creator; private set => SetProperty(ref _creator, value); }
    public string Description { get => _description; private set => SetProperty(ref _description, value); }
    public string Keywords { get => _keywords; private set => SetProperty(ref _keywords, value); }
    public string Copyright { get => _copyright; private set => SetProperty(ref _copyright, value); }
    public string MetadataError { get => _metadataError; private set => SetProperty(ref _metadataError, value); }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;
        await _catalog.InitializeAsync(cancellationToken);
        await _catalog.RecoverAsync(cancellationToken);
        var loaded = await _catalog.LoadAssetsAsync(cancellationToken);
        Assets.Clear();
        _knownPaths.Clear();
        foreach (var asset in MarkDuplicates(loaded))
        {
            Assets.Add(asset);
            _knownPaths.Add(Path.GetFullPath(asset.Path));
        }
        _initialized = true;
        StatusMessage = $"Catalog loaded. {Assets.Count} asset(s); {Assets.Count(a => a.Status == "Missing source file")} missing source file(s).";
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var candidates = new List<string>();
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (File.Exists(path)) candidates.Add(path);
            else if (Directory.Exists(path))
            {
                try
                {
                    candidates.AddRange(Directory.EnumerateFiles(path, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ReportError(path, $"Folder scan failed: {ex.Message}");
                }
            }
            else ReportError(path, "Source path does not exist or is inaccessible.");
        }

        var uniqueCandidates = candidates.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
        var added = 0;
        var skipped = 0;
        var failed = 0;
        IsImporting = true;
        ImportProgress = 0;
        try
        {
            for (var i = 0; i < uniqueCandidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = uniqueCandidates[i];
                ProgressMessage = $"Hashing and cataloguing {i + 1} of {uniqueCandidates.Count}: {Path.GetFileName(fullPath)}";
                ImportProgress = uniqueCandidates.Count == 0 ? 100 : (int)((i * 100L) / uniqueCandidates.Count);
                if (_knownPaths.Contains(fullPath))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var info = new FileInfo(fullPath);
                    var hash = await _fingerprints.ComputeSha256Async(fullPath, cancellationToken);
                    var matches = await _catalog.FindDuplicateAssetIdsAsync(hash, cancellationToken);
                    var imported = await _catalog.ImportAssetAsync(
                        new ImportedAsset(fullPath, info.Name, info.Length, "Ready", hash), cancellationToken);
                    _knownPaths.Add(fullPath);
                    Assets.Add(imported);
                    added++;
                    if (matches.Count > 0)
                    {
                        var index = Assets.Count - 1;
                        Assets[index] = imported with { Status = $"Duplicate content ({matches.Count} existing match(es))" };
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or Microsoft.Data.Sqlite.SqliteException)
                {
                    failed++;
                    ReportError(fullPath, $"Import failed: {ex.Message}");
                }
                ImportProgress = (int)(((i + 1L) * 100) / Math.Max(1, uniqueCandidates.Count));
            }
        }
        finally
        {
            IsImporting = false;
            if (uniqueCandidates.Count == 0) ImportProgress = 100;
        }

        var all = MarkDuplicates(Assets.ToList());
        Assets.Clear();
        foreach (var asset in all) Assets.Add(asset);
        StatusMessage = $"Import finished: {added} added, {skipped} already catalogued, {failed} failed. {Assets.Count} asset(s) in catalog.";
        ProgressMessage = failed == 0 ? "Import complete." : $"Import complete with {failed} error(s). See the error list.";
    }

    public async Task ReadMetadataAsync(ImportedAsset? asset, CancellationToken cancellationToken = default)
    {
        SelectedAsset = asset;
        MetadataItems.Clear();
        CaptureTimestamp = Creator = Description = Keywords = Copyright = "—";
        MetadataError = "";
        if (asset is null) return;

        try
        {
            using var document = await _metadataReader.ReadAsync(asset.Path, cancellationToken);
            var root = document.RootElement;
            var metadata = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                ? root[0]
                : root;
            if (metadata.ValueKind != JsonValueKind.Object) return;

            foreach (var property in metadata.EnumerateObject())
                MetadataItems.Add(new MetadataEntry(property.Name, FormatValue(property.Value)));

            CaptureTimestamp = FindValue(metadata, "DateTimeOriginal", "CreateDate", "MediaCreateDate", "DateCreated");
            Creator = FindValue(metadata, "Creator", "Artist", "By-line", "Author");
            Description = FindValue(metadata, "ImageDescription", "Description", "Caption-Abstract", "Title");
            Keywords = FindValue(metadata, "Keywords", "Subject");
            Copyright = FindValue(metadata, "Copyright", "Rights");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            MetadataError = $"Metadata unavailable: {ex.Message}";
            ReportError(asset.Path, MetadataError);
        }
    }

    private static IReadOnlyList<ImportedAsset> MarkDuplicates(IReadOnlyList<ImportedAsset> assets)
    {
        var duplicateHashes = assets.Where(a => !string.IsNullOrWhiteSpace(a.Sha256))
            .GroupBy(a => a.Sha256!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return assets.Select(a => duplicateHashes.Contains(a.Sha256 ?? "")
            ? a with { Status = File.Exists(a.Path) ? "Duplicate content" : "Missing source file" }
            : a with { Status = File.Exists(a.Path) ? "Ready" : "Missing source file" }).ToList();
    }

    private static string FindValue(JsonElement metadata, params string[] keys)
    {
        foreach (var property in metadata.EnumerateObject())
        {
            if (keys.Any(key => property.Name.EndsWith(":" + key, StringComparison.OrdinalIgnoreCase) ||
                                property.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                var value = FormatValue(property.Value);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return "—";
    }

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(FormatValue)),
        JsonValueKind.Object => value.GetRawText(),
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => value.ToString()
    };

    public void ReportUiError(string message) => ReportError("Application", message);

    private void ReportError(string path, string message)
    {
        Errors.Add($"{path}: {message}");
        StatusMessage = message;
    }
}
