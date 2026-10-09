# Asset identity migration plan (review only)

## Current behavior and risk

The current `Assets.AssetId` is derived from the normalized absolute path using SHA-256. It is deterministic and survives restarts, but it is not an immutable identity for the media asset:

- Moving or renaming a file changes the path and therefore creates a different ID.
- On macOS, the current normalization does not account for case-insensitive APFS volumes; paths that differ only in case can receive different IDs even when they resolve to the same file.
- Path normalization does not resolve symlinks, volume aliases, mount changes, or hard links.
- A content hash is not a suitable primary identity either: separate catalog entries may intentionally represent duplicate copies, and the media may change in the future if safe edits are introduced.

## Recommended staged migration

Do not rewrite primary keys in-place as a one-step deployment. Existing tables (`AssetFingerprints`, `MetadataSnapshots`, `Revisions`) reference `Assets.AssetId`; an uncoordinated key change could orphan provenance or revision history.

### Stage A — additive schema migration

1. Add nullable `CatalogAssetId TEXT` to `Assets` and a unique index.
2. In one SQLite transaction, assign a cryptographically random UUID to every existing row that lacks a `CatalogAssetId`. Generate each value once and persist it; never recompute it from path or content.
3. Add a `LegacyAssetIdMap(LegacyAssetId PRIMARY KEY, CatalogAssetId UNIQUE NOT NULL)` table and record every old path-derived ID to its new catalog ID.
4. Keep `AssetId` and all current foreign keys unchanged during this compatibility stage. Do not delete, merge, or rewrite existing records.
5. Validate row counts, non-null unique new IDs, map cardinality, and foreign-key integrity before committing the migration. If validation fails, roll back the whole migration.

### Stage B — dual-read / dual-write compatibility

1. New application code reads and displays `CatalogAssetId` as the stable identity.
2. Existing records are resolved through `LegacyAssetIdMap`; all fingerprint, snapshot, revision, and journal records remain linked to the legacy key until an explicit table migration.
3. New assets receive a random catalog-generated UUID before insertion. Path and fingerprint are indexed attributes, not identity.
4. Importing a path already in the catalog updates its observations without changing its catalog ID. Importing byte-identical content at another path remains a separate asset record and is linked through duplicate review, not merged automatically.

### Stage C — optional relational key cutover

Only after Stage B is deployed and backed up, use a versioned SQLite migration to rebuild the dependent tables with `CatalogAssetId` as their foreign key. Preserve the legacy mapping permanently (or for a documented retention period). Validate every row and foreign key before swapping tables. Test interruption before and after the table swap against a copy of a real catalog.

## Required migration tests

- Empty database migration.
- Migration with multiple existing assets and duplicate fingerprints.
- Stable UUIDs after repeated startup and repeated migration execution.
- Existing original filenames, paths, hashes, snapshots, revisions, and journal entries preserved.
- A path change does not change the catalog-generated ID after migration.
- Case-only path variations on case-insensitive and case-sensitive volumes.
- Foreign-key check returns no violations.
- Simulated interruption rolls back or safely resumes without losing legacy records.
- Backup/restore and downgrade behavior are explicitly defined before release.

## Decision for this PR

This PR does not implement the identity migration or rename files. Keep the existing path-derived IDs for compatibility, document their limitations, and schedule the additive migration as a separate, tested change before any move/rename workflow is enabled.
