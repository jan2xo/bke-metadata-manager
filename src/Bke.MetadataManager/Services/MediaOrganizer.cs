using System.Security.Cryptography;
using System.Text.Json;

namespace Bke.MetadataManager.Services;

public sealed record MovedAsset(
    string OriginalPath,
    string DestinationPath,
    string Category,
    string Event,
    string DateSource,
    string Sha256,
    long Bytes,
    DateTimeOffset MovedAtUtc);

public sealed record MoveOutcome(string DestinationPath, bool ManifestWritten, string? Warning = null);

public sealed class MediaOrganizer
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mxf", ".mkv", ".avi", ".mts", ".m2ts", ".mpg", ".mpeg",
        ".jpg", ".jpeg", ".png", ".heic", ".tif", ".tiff", ".dng", ".cr2", ".cr3", ".nef", ".arw",
        ".wav", ".aif", ".aiff", ".mp3", ".flac"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly CaptureDateReader _dates;
    private readonly SemaphoreSlim _manifestLock = new(1, 1);

    public MediaOrganizer(CaptureDateReader? dates = null) => _dates = dates ?? new CaptureDateReader();

    public async Task<MoveOutcome> MoveAsync(string source, string destinationRoot, string eventName, string category, CancellationToken ct = default)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("The dropped source file was not found.", source);
        if (string.IsNullOrWhiteSpace(destinationRoot) || !Directory.Exists(destinationRoot))
            throw new InvalidOperationException("Choose an existing destination folder in Settings first.");
        if (!SupportedExtensions.Contains(Path.GetExtension(source)))
            throw new InvalidDataException("Unsupported media type: " + Path.GetExtension(source));
        var safeEvent = PathNames.Segment(eventName);
        var safeCategory = PathNames.Segment(category);
        var date = _dates.Read(source);
        var eventDirectory = Path.Combine(Path.GetFullPath(destinationRoot), safeEvent);
        var targetDirectory = Path.Combine(eventDirectory, safeCategory);
        var target = Path.Combine(targetDirectory, PathNames.FileName(date.Value, Path.GetFileName(source), safeEvent, safeCategory));
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The source is already at the target path.");
        if (File.Exists(target)) throw new IOException("Target filename already exists; source was not moved: " + target);

        Directory.CreateDirectory(targetDirectory);
        var temp = Path.Combine(targetDirectory, "." + Guid.NewGuid().ToString("N") + ".partial");
        var targetPromoted = false;
        var sourceDeleted = false;
        string hash;
        try
        {
            // Stage in the destination filesystem. This works across volumes and never overwrites.
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            hash = await HashAsync(source, ct);
            var stagedHash = await HashAsync(temp, ct);
            if (!string.Equals(hash, stagedHash, StringComparison.Ordinal))
                throw new IOException("Verification failed. The source was preserved.");
            ct.ThrowIfCancellationRequested();
            File.Move(temp, target); // Atomic promotion within the destination; never overwrite.
            targetPromoted = true;
            try
            {
                File.Delete(source); // Only after destination copy is verified and promoted.
                sourceDeleted = true;
            }
            catch
            {
                // Source still exists; remove the newly created duplicate if possible.
                try { File.Delete(target); } catch { /* Both copies still survive; report error. */ }
                throw;
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        if (!targetPromoted || !sourceDeleted) throw new IOException("Move did not finish.");
        var record = new MovedAsset(source, target, safeCategory, safeEvent, date.Source, hash, new FileInfo(target).Length, DateTimeOffset.UtcNow);
        try
        {
            await AppendManifestAsync(eventDirectory, record, ct);
            return new MoveOutcome(target, true);
        }
        catch (Exception ex)
        {
            // The physical move succeeded. Never claim failure and tempt users to re-drop a missing file.
            return new MoveOutcome(target, false, "Moved successfully, but could not update manifest: " + ex.Message);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private async Task AppendManifestAsync(string eventDirectory, MovedAsset record, CancellationToken ct)
    {
        await _manifestLock.WaitAsync(ct);
        try
        {
            var manifest = Path.Combine(eventDirectory, ".media-organizer.json");
            List<MovedAsset> records = [];
            if (File.Exists(manifest))
                records = JsonSerializer.Deserialize<List<MovedAsset>>(await File.ReadAllTextAsync(manifest, ct), JsonOptions)
                    ?? throw new InvalidDataException("Event manifest is unreadable.");
            records.Add(record);
            var temp = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(records, JsonOptions), ct);
                File.Move(temp, manifest, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _manifestLock.Release(); }
    }
}
