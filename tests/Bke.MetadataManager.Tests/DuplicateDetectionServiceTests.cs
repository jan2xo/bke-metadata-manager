using Bke.MetadataManager.Services;
namespace Bke.MetadataManager.Tests;
public sealed class DuplicateDetectionServiceTests
{
    [Fact]
    public void ContentMatchIsIndependentOfFilename()
    {
        var finding = new DuplicateDetectionService().CreateContentMatch("abc123");
        Assert.Equal(DuplicateKind.ExactContentDuplicate, finding.Kind);
    }
    [Fact]
    public void MatchingCatalogAssetIsDistinguished()
    {
        var finding = new DuplicateDetectionService().CreateContentMatch("abc123", "asset-42");
        Assert.Equal(DuplicateKind.AlreadyCataloguedAsset, finding.Kind);
        Assert.Equal("asset-42", finding.MatchingAssetId);
    }
    [Fact]
    public void DifferentContentCanHaveIdenticalMetadata()
    {
        var service = new DuplicateDetectionService();
        Assert.True(service.HasIdenticalMetadata("{\"Title\":\"same\"}", "{\"Title\":\"same\"}"));
        Assert.False(service.HasIdenticalMetadata("{\"Title\":\"same\"}", "{\"Title\":\"other\"}"));
    }
}
