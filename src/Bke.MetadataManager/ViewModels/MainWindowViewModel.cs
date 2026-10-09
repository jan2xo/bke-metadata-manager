using System.Collections.ObjectModel;
using Bke.MetadataManager.Models;
using Bke.MetadataManager.Services;

namespace Bke.MetadataManager.ViewModels;

public sealed class MainWindowViewModel
{
    private readonly FileFingerprintService _fingerprints = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<ImportedAsset> Assets { get; } = new();
    public string StatusMessage { get; private set; } = "Ready. Import media to begin.";

    public async Task ImportPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        foreach (var path in paths)
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
                    Assets.Add(new ImportedAsset(path, Path.GetFileName(path), 0, $"Folder scan failed: {ex.Message}"));
                }
            }
        }

        var added = 0;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(candidate);
            if (!_knownPaths.Add(fullPath)) continue;
            try
            {
                var info = new FileInfo(fullPath);
                var asset = new ImportedAsset(fullPath, info.Name, info.Length, "Hashing…");
                Assets.Add(asset);
                var hash = await _fingerprints.ComputeSha256Async(fullPath, cancellationToken);
                var index = Assets.IndexOf(asset);
                if (index >= 0) Assets[index] = asset with { Status = "Ready", Sha256 = hash };
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var index = Assets.ToList().FindIndex(x => StringComparer.OrdinalIgnoreCase.Equals(x.Path, fullPath));
                var failed = new ImportedAsset(fullPath, Path.GetFileName(fullPath), 0, $"Import failed: {ex.Message}");
                if (index >= 0) Assets[index] = failed; else Assets.Add(failed);
            }
        }
        StatusMessage = $"Imported {added} new file(s). {Assets.Count} item(s) in queue.";
    }
}
