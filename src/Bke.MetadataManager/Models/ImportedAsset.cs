namespace Bke.MetadataManager.Models;

public sealed record ImportedAsset(
    string Path,
    string FileName,
    long SizeBytes,
    string Status,
    string? Sha256 = null,
    string? AssetId = null,
    string? OriginalFilename = null)
{
    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024d:0.0} KB"
        : $"{SizeBytes / (1024d * 1024d):0.0} MB";
}

public sealed record MetadataEntry(string Key, string Value);
