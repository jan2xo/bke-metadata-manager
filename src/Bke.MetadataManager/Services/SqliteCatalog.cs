using Microsoft.Data.Sqlite;
namespace Bke.MetadataManager.Services;
public sealed class SqliteCatalog(string databasePath)
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY, AppliedAtUtc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Assets (
                AssetId TEXT PRIMARY KEY, CurrentPath TEXT NOT NULL, OriginalPath TEXT NOT NULL,
                OriginalFilename TEXT NOT NULL, CurrentFilename TEXT NOT NULL, Format TEXT,
                SizeBytes INTEGER NOT NULL, ImportedAtUtc TEXT NOT NULL, CurrentRevision INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Available');
            CREATE TABLE IF NOT EXISTS AssetFingerprints (
                AssetId TEXT NOT NULL REFERENCES Assets(AssetId), Algorithm TEXT NOT NULL, Fingerprint TEXT NOT NULL,
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
}
