using Bke.MetadataManager.Services;
using Xunit;
namespace Bke.MetadataManager.Tests;
public sealed class FilenameGeneratorTests
{
    [Fact]
    public void Generate_UsesDateSanitizedEventSequenceAndExtension()
    {
        var sut = new FilenameGenerator();
        Assert.Equal("2026-10-05_CPIO-ANNIVERSARY_PHOTO_0001.jpg",
            sut.Generate(new DateOnly(2026, 10, 5), "CPIO Anniversary", "photo", 1, ".JPG"));
    }
    [Fact]
    public void Sanitize_RemovesUnsafeLeadingAndTrailingCharacters()
    {
        Assert.Equal("PRESS-CONFERENCE", FilenameGenerator.Sanitize(" !!Press   Conference!! "));
    }
    [Fact]
    public void PreviewBatch_RejectsExistingTargetWithoutTouchingIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var target = Path.Combine(dir, "2026-10-05_EVENT_PHOTO_0001.jpg");
            File.WriteAllText(target, "keep");
            var source = Path.Combine(dir, "source.jpg");
            File.WriteAllText(source, "source");
            Assert.Throws<IOException>(() => new FilenameGenerator().PreviewBatch(dir, new DateOnly(2026,10,5), "event", "photo", new[] { source }));
            Assert.Equal("keep", File.ReadAllText(target));
        }
        finally { Directory.Delete(dir, true); }
    }
}
