using System.Security.Cryptography;
using System.Text;
using Bke.MetadataManager.Models;
using Microsoft.Data.Sqlite;

namespace Bke.MetadataManager.Services;

/// <summary>
/// Local catalog. The journal protects database-side import bookkeeping only; it never
/// claims that a media edit was committed. This milestone performs no media writes.
/// </summary>
public sealed class SqliteCatalog(string databasePath)
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);
    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true,
        Pooling = false
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY, AppliedAtUtc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Assets (
                AssetId TEXT PRIMARY KEY, CurrentPath TEXT NOT NULL UNIQUE, OriginalPath TEXT NOT NULL,
                OriginalFilename TEXT NOT NULL, CurrentFilename TEXT NOT NULL, Format TEXT,
                SizeBytes INTEGER NOT NULL, ImportedAtUtc TEXT NOT NULL, CurrentRevision INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Available');
            CREATE TABLE IF NOT EXISTS AssetFingerprints (
                AssetId TEXT NOT NULL REFERENCES Assets(AssetId) ON DELETE CASCADE, Algorithm TEXT NOT NULL, Fingerprint TEXT NOT NULL,
                IsImmutableOriginal INTEGER NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (AssetId, Algorithm, Fingerprint));
            CREATE INDEX IF NOT EXISTS IX_AssetFingerprints_Fingerprint ON AssetFingerprints(Algorithm, Fingerprint);
            CREATE TABLE IF NOT EXISTS MetadataSnapshots (
                SnapshotId TEXT PRIMARY KEY, AssetId TEXT NOT NULL REFERENCES Assets(AssetId), RevisionNumber INTEGER NOT NULL,
                MetadataJson TEXT NOT NULL, CapturedAtUtc TEXT NOT NULL, Source TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Revisions (
                RevisionId TEXT PRIMARY KEY, AssetId TEXT NOT NULL REFERENCES Assets(AssetId), RevisionNumber INTEGER NOT NULL,
                PreviousPath TEXT NOT NULL, PreviousFilename TEXT NOT NULL, NewPath TEXT NOT NULL, NewFilename TEXT NOT NULL,
                PreviousMetadataSnapshotId TEXT REFERENCES MetadataSnapshots(SnapshotId), ChangeJson TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL, Status TEXT NOT NULL, UNIQUE (AssetId, RevisionNumber));
            CREATE TABLE IF NOT EXISTS ImportSessions (
                ImportSessionId TEXT PRIMARY KEY, StartedAtUtc TEXT NOT NULL, CompletedAtUtc TEXT,
                ImportedCount INTEGER NOT NULL DEFAULT 0, ErrorCount INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS OperationJournal (
                OperationId TEXT PRIMARY KEY, AssetId TEXT, OperationType TEXT NOT NULL, StartedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT, State TEXT NOT NULL, DetailsJson TEXT NOT NULL, ErrorMessage TEXT);
            INSERT OR IGNORE INTO SchemaMigrations(Version, AppliedAtUtc) VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ','now'));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var journal = connection.CreateCommand())
        {
            journal.Transaction = (SqliteTransaction)transaction;
            journal.CommandText = """
                UPDATE OperationJournal
                SET State='Interrupted', CompletedAtUtc=$now,
                    ErrorMessage=COALESCE(ErrorMessage,'Application stopped before operation completion')
                WHERE State IN ('Started','Prepared');
                """;
            journal.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await journal.ExecuteNonQueryAsync(cancellationToken);
        }

        var rows = new List<(string Id, string Path)>();
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = (SqliteTransaction)transaction;
            query.CommandText = "SELECT AssetId, CurrentPath FROM Assets;";
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }
        foreach (var row in rows)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE Assets SET Status=$status WHERE AssetId=$id;";
            update.Parameters.AddWithValue("$status", File.Exists(row.Path) ? "Available" : "Missing");
            update.Parameters.AddWithValue("$id", row.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ImportedAsset> ImportAssetAsync(ImportedAsset incoming, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(incoming.Path);
        var assetId = CreateStableAssetId(fullPath);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var operationId = Guid.NewGuid().ToString("N");

        // Commit the journal start independently so recovery can see an interrupted import.
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var journal = connection.CreateCommand();
            journal.CommandText = """
                INSERT INTO OperationJournal(OperationId,AssetId,OperationType,StartedAtUtc,State,DetailsJson)
                VALUES($op,$asset,'Import',$now,'Started',$details);
                """;
            journal.Parameters.AddWithValue("$op", operationId);
            journal.Parameters.AddWithValue("$asset", assetId);
            journal.Parameters.AddWithValue("$now", now);
            journal.Parameters.AddWithValue("$details", System.Text.Json.JsonSerializer.Serialize(new { path = fullPath, sha256 = incoming.Sha256 }));
            await journal.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            string? existingId = null;
            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = (SqliteTransaction)transaction;
                lookup.CommandText = "SELECT AssetId FROM Assets WHERE CurrentPath=$path;";
                lookup.Parameters.AddWithValue("$path", fullPath);
                existingId = await lookup.ExecuteScalarAsync(cancellationToken) as string;
            }
            if (existingId is not null) assetId = existingId;

            await using (var asset = connection.CreateCommand())
            {
                asset.Transaction = (SqliteTransaction)transaction;
                asset.CommandText = """
                    INSERT INTO Assets(AssetId,CurrentPath,OriginalPath,OriginalFilename,CurrentFilename,Format,SizeBytes,ImportedAtUtc,Status)
                    VALUES($id,$path,$path,$name,$name,$format,$size,$now,'Available')
                    ON CONFLICT(AssetId) DO UPDATE SET
                        CurrentPath=excluded.CurrentPath, CurrentFilename=excluded.CurrentFilename,
                        Format=excluded.Format, SizeBytes=excluded.SizeBytes, Status='Available';
                    """;
                asset.Parameters.AddWithValue("$id", assetId);
                asset.Parameters.AddWithValue("$path", fullPath);
                asset.Parameters.AddWithValue("$name", Path.GetFileName(fullPath));
                asset.Parameters.AddWithValue("$format", Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant());
                asset.Parameters.AddWithValue("$size", incoming.SizeBytes);
                asset.Parameters.AddWithValue("$now", now);
                await asset.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(incoming.Sha256))
            {
                await using var fingerprint = connection.CreateCommand();
                fingerprint.Transaction = (SqliteTransaction)transaction;
                fingerprint.CommandText = """
                    INSERT OR IGNORE INTO AssetFingerprints(AssetId,Algorithm,Fingerprint,IsImmutableOriginal,CreatedAtUtc)
                    SELECT $id,'SHA-256',$hash,CASE WHEN EXISTS(SELECT 1 FROM AssetFingerprints WHERE AssetId=$id AND IsImmutableOriginal=1) THEN 0 ELSE 1 END,$now;
                    """;
                fingerprint.Parameters.AddWithValue("$id", assetId);
                fingerprint.Parameters.AddWithValue("$hash", incoming.Sha256);
                fingerprint.Parameters.AddWithValue("$now", now);
                await fingerprint.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var finish = connection.CreateCommand())
            {
                finish.Transaction = (SqliteTransaction)transaction;
                finish.CommandText = "UPDATE OperationJournal SET State='Completed',CompletedAtUtc=$now WHERE OperationId=$op;";
                finish.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                finish.Parameters.AddWithValue("$op", operationId);
                await finish.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return incoming with { Path = fullPath, FileName = Path.GetFileName(fullPath), AssetId = assetId, OriginalFilename = Path.GetFileName(fullPath), Status = "Ready" };
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(operationId, ex.Message, CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<ImportedAsset>> LoadAssetsAsync(CancellationToken cancellationToken = default)
    {
        var assets = new List<ImportedAsset>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.AssetId,a.CurrentPath,a.OriginalFilename,a.SizeBytes,a.Status,
                   (SELECT f.Fingerprint FROM AssetFingerprints f WHERE f.AssetId=a.AssetId AND f.Algorithm='SHA-256' AND f.IsImmutableOriginal=1 ORDER BY f.CreatedAtUtc LIMIT 1)
            FROM Assets a ORDER BY a.ImportedAtUtc,a.OriginalFilename;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var path = reader.GetString(1);
            var exists = File.Exists(path);
            assets.Add(new ImportedAsset(path, Path.GetFileName(path), reader.GetInt64(3),
                exists ? "Ready" : "Missing source file",
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(0), reader.GetString(2)));
        }
        return assets;
    }

    public async Task<IReadOnlyList<string>> FindDuplicateAssetIdsAsync(string sha256, CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT AssetId FROM AssetFingerprints WHERE Algorithm='SHA-256' AND Fingerprint=$hash AND IsImmutableOriginal=1;";
        command.Parameters.AddWithValue("$hash", sha256);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetString(0));
        return ids;
    }

    public static string CreateStableAssetId(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath).Normalize(NormalizationForm.FormC);
        if (OperatingSystem.IsWindows()) normalized = normalized.ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "asset-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task MarkOperationFailedAsync(string operationId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE OperationJournal SET State='Failed',CompletedAtUtc=$now,ErrorMessage=$error WHERE OperationId=$op;";
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$error", message);
            command.Parameters.AddWithValue("$op", operationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException) { /* Preserve the original import exception; startup recovery will reconcile a still-open journal row. */ }
    }
}
