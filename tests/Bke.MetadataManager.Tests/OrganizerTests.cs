using Bke.MetadataManager.Services;
using Xunit;

namespace Bke.MetadataManager.Tests;

public sealed class OrganizerTests
{
    [Fact]
    public void Filename_Uses_Date_Original_Event_Category()
    {
        var result = PathNames.FileName(new DateTime(2026, 11, 6), "C0123.MP4", "NHLB", "Drone");
        Assert.Equal("2026-11-06-C0123-NHLB-Drone.MP4", result);
    }

    [Theory]
    [InlineData("Cagayan Anniversary", "Cagayan-Anniversary")]
    [InlineData("../bad/name", "bad-name")]
    [InlineData("Drone", "Drone")]
    public void Segment_Sanitizes_Folder_Parts(string input, string expected) =>
        Assert.Equal(expected, PathNames.Segment(input));

    [Fact]
    public void Categories_Are_Stored_Without_Database()
    {
        using var dir = new TestDirectory();
        var store = new SettingsStore(dir.Path);
        var settings = store.Load();
        Assert.Equal(3, settings.Categories.Count);
        settings.DestinationFolder = dir.Path;
        settings.Categories.Add("Interview");
        store.Save(settings);
        Assert.Equal(dir.Path, store.Load().DestinationFolder);
        Assert.Contains("Interview", store.Load().Categories);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public async Task Move_Verifies_And_Deletes_Source_Then_Writes_Manifest()
    {
        using var dir = new TestDirectory();
        var source = System.IO.Path.Combine(dir.Path, "C0123.MP4");
        var destination = System.IO.Path.Combine(dir.Path, "organized");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(source, "mock video content for safe move test");
        File.SetLastWriteTime(source, new DateTime(2026, 11, 6, 12, 0, 0));
        var outcome = await new MediaOrganizer().MoveAsync(source, destination, "NHLB", "Drone");
        var target = System.IO.Path.Combine(destination, "NHLB", "Drone", "2026-11-06-C0123-NHLB-Drone.MP4");
        Assert.Equal(target, outcome.DestinationPath);
        Assert.True(outcome.ManifestWritten);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(source));
        Assert.Equal("mock video content for safe move test", await File.ReadAllTextAsync(target));
        Assert.Contains("C0123.MP4", await File.ReadAllTextAsync(System.IO.Path.Combine(destination, "NHLB", ".media-organizer.json")));
    }

    [Fact]
    public async Task Collision_Refuses_Move_And_Leaves_Source_On_Plate()
    {
        using var dir = new TestDirectory();
        var source = System.IO.Path.Combine(dir.Path, "C0123.MP4");
        var destination = System.IO.Path.Combine(dir.Path, "organized");
        var targetDir = System.IO.Path.Combine(destination, "NHLB", "Drone");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(source, "source data");
        File.SetLastWriteTime(source, new DateTime(2026, 11, 6, 12, 0, 0));
        var target = System.IO.Path.Combine(targetDir, "2026-11-06-C0123-NHLB-Drone.MP4");
        await File.WriteAllTextAsync(target, "existing");
        await Assert.ThrowsAsync<IOException>(() => new MediaOrganizer().MoveAsync(source, destination, "NHLB", "Drone"));
        Assert.True(File.Exists(source));
        Assert.Equal("existing", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task No_Destination_Preserves_Source()
    {
        using var dir = new TestDirectory();
        var source = System.IO.Path.Combine(dir.Path, "C0123.MP4");
        await File.WriteAllTextAsync(source, "footage");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MediaOrganizer().MoveAsync(source, "", "NHLB", "Drone"));
        Assert.True(File.Exists(source));
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bke-media-test-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
