using Bke.MetadataManager.Services;
using Bke.MetadataManager.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Bke.MetadataManager.Tests;

public sealed class SqliteCatalogIntegrationTests
{
    [Fact]
    public async Task ImportPersistsAcrossRestartAndRepeatedImportKeepsStableIdentity()
    {
        var root = NewTempDirectory();
        var source = Path.Combine(root, "original-name.bin");
        var database = Path.Combine(root, "catalog.db");
        var originalBytes = new byte[] { 10, 20, 30, 40, 50, 60 };
        await File.WriteAllBytesAsync(source, originalBytes);
        var originalHash = await new FileFingerprintService().ComputeSha256Async(source);

        try
        {
            var first = new MainWindowViewModel(database);
            await first.ImportPathsAsync(new[] { source });
            Assert.Single(first.Assets);
            var firstAsset = first.Assets[0];
            Assert.NotNull(firstAsset.AssetId);
            Assert.Equal("original-name.bin", firstAsset.OriginalFilename);
            Assert.Equal(originalHash, firstAsset.Sha256);

            await first.ImportPathsAsync(new[] { source });
            Assert.Single(first.Assets);

            var reopened = new MainWindowViewModel(database);
            await reopened.InitializeAsync();
            var loaded = Assert.Single(reopened.Assets);
            Assert.Equal(firstAsset.AssetId, loaded.AssetId);
            Assert.Equal("original-name.bin", loaded.OriginalFilename);
            Assert.Equal(originalHash, loaded.Sha256);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task IdenticalContentAtDifferentPathsIsRecognizedAsDuplicateWithoutWritingMedia()
    {
        var root = NewTempDirectory();
        var firstPath = Path.Combine(root, "one.bin");
        var secondPath = Path.Combine(root, "different-name.bin");
        var database = Path.Combine(root, "catalog.db");
        var bytes = Enumerable.Range(0, 2048).Select(i => (byte)(i % 251)).ToArray();
        await File.WriteAllBytesAsync(firstPath, bytes);
        await File.WriteAllBytesAsync(secondPath, bytes);
        var firstHash = await new FileFingerprintService().ComputeSha256Async(firstPath);
        var secondHash = await new FileFingerprintService().ComputeSha256Async(secondPath);

        try
        {
            Assert.Equal(firstHash, secondHash);
            var vm = new MainWindowViewModel(database);
            await vm.ImportPathsAsync(new[] { firstPath, secondPath });

            Assert.Equal(2, vm.Assets.Count);
            Assert.All(vm.Assets, asset => Assert.Equal("Duplicate content", asset.Status));
            Assert.NotEqual(vm.Assets[0].AssetId, vm.Assets[1].AssetId);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(firstPath));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(secondPath));
            Assert.Equal(firstHash, await new FileFingerprintService().ComputeSha256Async(firstPath));
            Assert.Equal(secondHash, await new FileFingerprintService().ComputeSha256Async(secondPath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RecoveryMarksInterruptedImportAndMissingSourceWithoutCreatingAsset()
    {
        var root = NewTempDirectory();
        var database = Path.Combine(root, "catalog.db");
        var missingSource = Path.Combine(root, "removed.bin");
        try
        {
            var catalog = new SqliteCatalog(database);
            await catalog.InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString()))
            {
                await connection.OpenAsync();
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO OperationJournal(OperationId,AssetId,OperationType,StartedAtUtc,State,DetailsJson)
                    VALUES('interrupted-test','asset-not-committed','Import',$now,'Started','{"path":"removed.bin"}');
                    """;
                insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync();
            }

            await catalog.RecoverAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString()))
            {
                await connection.OpenAsync();
                await using var query = connection.CreateCommand();
                query.CommandText = "SELECT State FROM OperationJournal WHERE OperationId='interrupted-test';";
                Assert.Equal("Interrupted", await query.ExecuteScalarAsync());
            }
            Assert.Empty(await catalog.LoadAssetsAsync());

            await File.WriteAllTextAsync(missingSource, "temporary");
            var hash = await new FileFingerprintService().ComputeSha256Async(missingSource);
            await catalog.ImportAssetAsync(new Bke.MetadataManager.Models.ImportedAsset(
                missingSource, Path.GetFileName(missingSource), new FileInfo(missingSource).Length, "Ready", hash));
            File.Delete(missingSource);
            await catalog.RecoverAsync();
            var missing = Assert.Single(await catalog.LoadAssetsAsync());
            Assert.Equal("Missing source file", missing.Status);
            Assert.Equal("removed.bin", missing.OriginalFilename);
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
