namespace Bke.MetadataManager.Services;
public enum DuplicateKind { ExactContentDuplicate, AlreadyCataloguedAsset, FilenameCollision, IdenticalMetadataOnly }
public sealed record DuplicateFinding(DuplicateKind Kind, string Message, string? MatchingAssetId = null);
/// <summary>Pure deterministic comparisons; the caller supplies computed fingerprints.</summary>
public sealed class DuplicateDetectionService
{
    public DuplicateFinding CreateContentMatch(string sha256, string? matchingAssetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        return matchingAssetId is null
            ? new(DuplicateKind.ExactContentDuplicate, "Exact content hash match.")
            : new(DuplicateKind.AlreadyCataloguedAsset, "This content is already in the catalog.", matchingAssetId);
    }
    public bool HasFilenameCollision(string targetPath) => File.Exists(targetPath) || Directory.Exists(targetPath);
    public bool HasIdenticalMetadata(string leftCanonicalJson, string rightCanonicalJson) => StringComparer.Ordinal.Equals(leftCanonicalJson, rightCanonicalJson);
}
