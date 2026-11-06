using System.Text.RegularExpressions;

namespace Bke.MetadataManager.Services;

public static partial class PathNames
{
    [GeneratedRegex(@"[^\p{L}\p{N}_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidParts();

    public static string Segment(string? input)
    {
        var value = InvalidParts().Replace((input ?? "").Trim(), "-").Trim('-', '_', '.');
        if (value.Length > 70) value = value[..70].TrimEnd('-', '_');
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..")
            throw new ArgumentException("An event/category must have at least one valid letter or number.");
        return value;
    }

    public static string FileName(DateTime recorded, string originalName, string eventName, string category)
    {
        var original = Segment(Path.GetFileNameWithoutExtension(originalName));
        var extension = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12 || !extension[1..].All(char.IsLetterOrDigit))
            throw new ArgumentException("An extension is required.");
        return $"{recorded:yyyy-MM-dd}-{original}-{Segment(eventName)}-{Segment(category)}{extension}";
    }
}
