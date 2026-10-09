using Bke.MetadataManager.Services;
using Xunit;
namespace Bke.MetadataManager.Tests;
public sealed class FileFingerprintServiceTests
{
    [Fact]
    public async Task SameBytesHaveSameFingerprintRegardlessOfFilename()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var left = Path.Combine(dir, "left.bin");
        var right = Path.Combine(dir, "renamed.bin");
        await File.WriteAllBytesAsync(left, new byte[] { 1, 2, 3, 4, 5 });
        await File.WriteAllBytesAsync(right, new byte[] { 1, 2, 3, 4, 5 });
        try
        {
            var service = new FileFingerprintService();
            Assert.Equal(await service.ComputeSha256Async(left), await service.ComputeSha256Async(right));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await File.ReadAllBytesAsync(left));
        }
        finally { Directory.Delete(dir, true); }
    }
}
