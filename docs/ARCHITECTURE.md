# Architecture — MVP 0.1.0

## Layers
- Avalonia desktop shell; UI must not mutate media directly.
- Services: SHA-256 fingerprinting, duplicate classification, filename preview, ExifTool read adapter, SQLite catalog initialization.
- Local SQLite persistence for assets, fingerprints, metadata snapshots, revisions, import sessions, operation journal.
- ExifTool is invoked using argument-list APIs rather than shell interpolation and in read mode only.

## Safety invariants
1. Media streams are never opened for write by the fingerprint service.
2. The ExifTool adapter exposes read-only operations; embedded writes are not implemented.
3. Filename generation is preview-only. A collision aborts preview and never overwrites a target.
4. Store the import-time hash as immutable provenance. Compute a new current hash after any permitted embedded metadata edit.
5. Before writes are enabled, add journal intent, metadata snapshot, validation, safe sidecar/embedded commit, reread verification, revision recording, and startup recovery.
6. Unsupported or unsafe formats must use XMP sidecars or remain read-only.

## Duplicate semantics
- Exact content duplicate: identical full-file SHA-256 regardless of filename.
- Already catalogued: a fingerprint matches an existing asset record.
- Filename collision: target path exists or batch entries collide under case-insensitive comparison.
- Identical metadata: canonical metadata representations match while content hashes differ; not a content duplicate.

## Planned phases
1. Foundation and schema (this branch).
2. Import queue, recursive folders, thumbnails and asset browser.
3. Catalog persistence and import-session recovery.
4. Duplicate review and filename preview UI.
5. Metadata editor and snapshot-first sidecar commits.
6. Revision browsing, restore and commit verification.
7. Automated tests and packaging.
