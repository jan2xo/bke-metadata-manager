using System.Text.Json;
using Bke.MetadataManager.Services;
using Bke.MetadataManager.ViewModels;
using Xunit;

namespace Bke.MetadataManager.Tests;

public sealed class ExifToolRuntimeTests
{
    [Fact]
    public async Task MissingExifToolProducesActionableErrorInMetadataViewer()
    {
        var root = NewTempDirectory();
        var media = Path.Combine(root, "sample.jpg");
        await File.WriteAllBytesAsync(media, new byte[] { 1, 2, 3, 4 });
        try
        {
            var vm = new MainWindowViewModel(Path.Combine(root, "catalog.db"), Path.Combine(root, "definitely-missing-exiftool"));
            await vm.ReadMetadataAsync(new Bke.MetadataManager.Models.ImportedAsset(media, "sample.jpg", 4, "Ready"));
            Assert.Contains("Install it or configure its executable path", vm.MetadataError, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(vm.Errors);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExifToolReadsAllConfiguredRepresentativeMediaFormatsWhenRuntimeFixturesAreProvided()
    {
        var fixtureDirectory = Environment.GetEnvironmentVariable("BKE_MEDIA_FIXTURE_DIR");
        var exifToolPath = Environment.GetEnvironmentVariable("EXIFTOOL_PATH");
        if (string.IsNullOrWhiteSpace(fixtureDirectory) || string.IsNullOrWhiteSpace(exifToolPath))
            return; // Real-format integration is exercised by the macOS Apple Silicon workflow.

        var reader = new ExifToolMetadataReader(exifToolPath);
        foreach (var extension in new[] { "jpg", "png", "heic", "mp4", "mov" })
        {
            var path = Path.Combine(fixtureDirectory, $"sample.{extension}");
            Assert.True(File.Exists(path), $"Missing runtime fixture: {path}");
            using var document = await reader.ReadAsync(path);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.True(document.RootElement.GetArrayLength() > 0, $"ExifTool returned no metadata for {extension}.");
            var record = document.RootElement[0];
            Assert.Equal(JsonValueKind.Object, record.ValueKind);
            Assert.Contains(record.EnumerateObject(), property =>
                property.Name.EndsWith(":FileType", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("FileType", StringComparison.OrdinalIgnoreCase));
        }

        var jpeg = Path.Combine(fixtureDirectory, "sample.jpg");
        var viewModel = new MainWindowViewModel(
            Path.Combine(Path.GetTempPath(), $"bke-metadata-test-{Guid.NewGuid():N}.db"),
            exifToolPath);
        await viewModel.ReadMetadataAsync(new Bke.MetadataManager.Models.ImportedAsset(
            jpeg, Path.GetFileName(jpeg), new FileInfo(jpeg).Length, "Ready"));
        Assert.Equal("2024:01:02 03:04:05", viewModel.CaptureTimestamp);
        Assert.Equal("BKE Runtime Test", viewModel.Creator);
        Assert.Equal("Read-only metadata fixture", viewModel.Description);
        Assert.Contains("runtime", viewModel.Keywords, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("BKE test only", viewModel.Copyright);
    }

    [Fact]
    public async Task MalformedMediaDoesNotEscapeMetadataViewerAsAnUnhandledException()
    {
        var exifToolPath = Environment.GetEnvironmentVariable("EXIFTOOL_PATH");
        if (string.IsNullOrWhiteSpace(exifToolPath)) return;
        var root = NewTempDirectory();
        var media = Path.Combine(root, "malformed.jpg");
        await File.WriteAllTextAsync(media, "not a real JPEG");
        try
        {
            var vm = new MainWindowViewModel(Path.Combine(root, "catalog.db"), exifToolPath);
            var exception = await Record.ExceptionAsync(() =>
                vm.ReadMetadataAsync(new Bke.MetadataManager.Models.ImportedAsset(media, "malformed.jpg", 14, "Ready")));
            Assert.Null(exception);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
