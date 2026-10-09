using System.Text;
namespace Bke.MetadataManager.Services;
public sealed class FilenameGenerator
{
    public string Generate(DateOnly eventDate, string eventName, string mediaType, int sequence, string extension)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        if (ext.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) throw new ArgumentException("Extension contains invalid characters.", nameof(extension));
        return $"{eventDate:yyyy-MM-dd}_{Sanitize(eventName)}_{Sanitize(mediaType)}_{sequence:0000}{ext.ToLowerInvariant()}";
    }
    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        var separator = false;
        foreach (var c in value.Trim().ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) { builder.Append(c); separator = false; }
            else if (!separator && builder.Length > 0) { builder.Append('-'); separator = true; }
        }
        return builder.ToString().Trim('-');
    }
    public IReadOnlyList<string> PreviewBatch(string directory, DateOnly date, string eventName, string mediaType, IEnumerable<string> sourcePaths)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        var index = 1;
        foreach (var source in sourcePaths)
        {
            var candidate = Generate(date, eventName, mediaType, index++, Path.GetExtension(source));
            var fullPath = Path.GetFullPath(Path.Combine(directory, candidate));
            if (File.Exists(fullPath) || Directory.Exists(fullPath) || !used.Add(fullPath))
                throw new IOException($"Filename collision; no files were changed: {fullPath}");
            results.Add(fullPath);
        }
        return results;
    }
}
