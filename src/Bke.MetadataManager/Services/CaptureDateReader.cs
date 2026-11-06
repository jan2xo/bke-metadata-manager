using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Bke.MetadataManager.Services;

public sealed record CaptureDate(DateTime Value, string Source);

public sealed class CaptureDateReader
{
    private static readonly string[] Tags =
    [
        "Date/Time Original",
        "Date/Time Digitized",
        "Date Created",
        "Creation Date",
        "Creation Time",
        "Create Date",
        "Media Create Date",
        "Track Create Date"
    ];

    public CaptureDate Read(string path)
    {
        // Metadata first; on malformed or unsupported media fall back to the filesystem.
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path).ToList();
            foreach (var wanted in Tags)
            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                if (!string.Equals(tag.Name, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                if (directory.TryGetDateTime(tag.Type, out var date) && Plausible(date))
                    return new CaptureDate(date, $"embedded:{directory.Name}/{tag.Name}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Safe documented fallback. No media content is ever modified.
        }
        var modified = File.GetLastWriteTime(path);
        if (!Plausible(modified)) modified = DateTime.Today;
        return new CaptureDate(modified, "filesystem:last-write-time");
    }

    private static bool Plausible(DateTime value) =>
        value.Year >= 1990 && value.Year <= DateTime.Today.Year + 1;
}
