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
        foreach (var extension in RuntimeExtensions())
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

        // Exercise the real media files through import, metadata reading, restart, and reimport.
        var mediaPaths = RuntimeExtensions()
            .Select(extension => Path.Combine(fixtureDirectory, $"sample.{extension}"))
            .ToArray();
        var fingerprints = new FileFingerprintService();
        var before = new Dictionary<string, string>();
        foreach (var path in mediaPaths)
            before[path] = await fingerprints.ComputeSha256Async(path);

        var catalogRoot = NewTempDirectory();
        try
        {
            var duplicateCopy = Path.Combine(catalogRoot, "same-bytes-different-name.jpg");
            File.Copy(Path.Combine(fixtureDirectory, "sample.jpg"), duplicateCopy);
            var database = Path.Combine(catalogRoot, "catalog.db");
            var catalogVm = new MainWindowViewModel(database, exifToolPath);
            await catalogVm.ImportPathsAsync(mediaPaths.Append(duplicateCopy));
            Assert.Equal(6, catalogVm.Assets.Count);
            Assert.Contains(catalogVm.Assets, asset => asset.FileName == "same-bytes-different-name.jpg" && asset.Status.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

            foreach (var asset in catalogVm.Assets)
                await catalogVm.ReadMetadataAsync(asset);

            var reopened = new MainWindowViewModel(database, exifToolPath);
            await reopened.InitializeAsync();
            Assert.Equal(6, reopened.Assets.Count);
            await reopened.ImportPathsAsync(mediaPaths.Append(duplicateCopy));
            Assert.Equal(6, reopened.Assets.Count);

            foreach (var path in mediaPaths)
                Assert.Equal(before[path], await fingerprints.ComputeSha256Async(path));
            Assert.Equal(before[Path.Combine(fixtureDirectory, "sample.jpg")],
                await fingerprints.ComputeSha256Async(duplicateCopy));
        }
        finally { Directory.Delete(catalogRoot, true); }
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

    private static string[] RuntimeExtensions() =>
        (Environment.GetEnvironmentVariable("BKE_MEDIA_FIXTURE_EXTENSIONS") ?? "jpg,png,heic,mp4,mov")
        .Split(\',\', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
