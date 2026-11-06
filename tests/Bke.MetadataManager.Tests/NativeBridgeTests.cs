using System.Text.Json;
using Bke.MetadataManager.Services;
using Xunit;

namespace Bke.MetadataManager.Tests;

public sealed class NativeBridgeTests
{
    [Fact]
    public async Task JsonBridge_UsesExistingVerifiedMoverAndReturnsSuccessfulResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "bke-native-bridge-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "sorted");
        var source = Path.Combine(root, "C0123.MP4");
        try
        {
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(source, "fixture data");
            File.SetLastWriteTime(source, new DateTime(2026, 11, 6, 12, 0, 0));
            var input = JsonSerializer.Serialize(new
            {
                eventName = "NHLB", category = "Drone",
                destinationFolder = destination, paths = new[] { source }
            });
            var response = await OrganizerBridge.ProcessJsonAsync(input);
            using var doc = JsonDocument.Parse(response);
            var item = doc.RootElement.GetProperty("results")[0];
            Assert.Equal(JsonValueKind.Null, item.GetProperty("error").ValueKind);
            var moved = item.GetProperty("destinationPath").GetString();
            Assert.NotNull(moved);
            Assert.EndsWith("2026-11-06-C0123-NHLB-Drone.MP4", moved!);
            Assert.True(File.Exists(moved));
            Assert.False(File.Exists(source));
            Assert.Equal("fixture data", await File.ReadAllTextAsync(moved!));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task JsonBridge_RejectsMissingRequiredContext()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => OrganizerBridge.ProcessJsonAsync(
            """{"eventName":"","category":"Drone","destinationFolder":"/tmp","paths":["x.mp4"]}"""));
    }

    [Fact]
    public async Task JsonBridge_FailingFileDoesNotPreventLaterFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "bke-bridge-batch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(root, "out");
            Directory.CreateDirectory(output);
            var valid = Path.Combine(root, "C0999.MP4");
            await File.WriteAllTextAsync(valid, "safe bytes");
            File.SetLastWriteTime(valid, new DateTime(2026, 11, 6));
            var input = JsonSerializer.Serialize(new
            {
                eventName = "NHLB", category = "Detail",
                destinationFolder = output, paths = new[] { Path.Combine(root, "missing.mp4"), valid }
            });
            var response = await OrganizerBridge.ProcessJsonAsync(input);
            using var doc = JsonDocument.Parse(response);
            var results = doc.RootElement.GetProperty("results");
            Assert.Equal(2, results.GetArrayLength());
            Assert.NotEqual(JsonValueKind.Null, results[0].GetProperty("error").ValueKind);
            Assert.Equal(JsonValueKind.Null, results[1].GetProperty("error").ValueKind);
            Assert.False(File.Exists(valid));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
