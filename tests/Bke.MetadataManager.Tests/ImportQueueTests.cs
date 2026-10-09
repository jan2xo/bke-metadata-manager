using Bke.MetadataManager.ViewModels;
using Xunit;

namespace Bke.MetadataManager.Tests;

public sealed class ImportQueueTests
{
    [Fact]
    public async Task ImportPaths_RecursivelyIndexesFilesAndSkipsRepeatedImports()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        var first = Path.Combine(root, "same-name.dat");
        var second = Path.Combine(nested, "same-name.dat");
        await File.WriteAllTextAsync(first, "first content");
        await File.WriteAllTextAsync(second, "different content");
        try
        {
            var vm = new MainWindowViewModel();
            await vm.ImportPathsAsync(new[] { root });
            Assert.Equal(2, vm.Assets.Count);
            Assert.All(vm.Assets, asset => Assert.Equal("Ready", asset.Status));
            Assert.NotEqual(vm.Assets[0].Sha256, vm.Assets[1].Sha256);
            await vm.ImportPathsAsync(new[] { root, first });
            Assert.Equal(2, vm.Assets.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
