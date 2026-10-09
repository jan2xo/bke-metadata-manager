# Architecture — MVP 0.1.0

## Implemented layers
- Avalonia desktop shell with file/folder pickers and an OS drag-and-drop import target.
- Import view model recursively enumerates files, reports errors/progress, and computes SHA-256 using read-only file access.
- SQLite catalog persists stable path-derived asset IDs, original filenames, import-time SHA-256 fingerprints, and source availability.
- OperationJournal records each import start and terminal state. Asset/fingerprint writes and journal completion share one SQLite transaction.
- Startup recovery marks leftover Started/Prepared journal entries Interrupted and reconciles catalogued source files as Available or Missing.
- ExifTool is invoked with argument-list APIs and read-only JSON output. The UI shows selected-file tags when ExifTool is installed.

## Safety invariants
1. Media streams are never opened for write by the fingerprint service or metadata reader.
2. The ExifTool adapter exposes read-only operations; embedded writes are not implemented.
3. Filename generation is preview-only. A collision aborts preview and never overwrites a target.
4. The first imported SHA-256 is immutable provenance. Later fingerprints are not mislabeled as the original.
5. An import is not a metadata modification commit. There are no metadata write, bulk edit, or rename commands.
6. Before writes are enabled, add journal intent, metadata snapshot, validation, safe sidecar/embedded commit, reread verification, revision recording, and startup recovery.
7. Unsupported or unsafe formats must use XMP sidecars or remain read-only.

## Duplicate semantics
- Exact content duplicate: identical immutable full-file SHA-256 regardless of filename.
- Already catalogued: a source path already represented in the catalog is not added again.
- Filename collision: target path exists or batch entries collide under case-insensitive comparison; preview is rejected.
- Identical metadata: canonical metadata representations match while content hashes differ; not a content duplicate.

## Recovery boundaries
- SQLite transactions prevent an asset/fingerprint row from being committed without the corresponding completed journal state.
- The Started journal row is committed before the import transaction. If the process stops between those steps, startup marks the journal row Interrupted.
- Missing source files remain in the catalog and are labelled Missing source file; catalog data is not silently deleted.
- Recovery currently reconciles import bookkeeping only. No filesystem/media mutation is performed or rolled back.

## Known limitations
- ExifTool is an external dependency and must be installed/configured separately (EXIFTOOL_PATH can point to its executable).
- Stable asset IDs are derived from normalized full paths; they are stable across restart, but a manually moved file is treated as a different path until move tracking exists.
- The import catalog currently uses one SQLite database per user profile; cloud sync and concurrent multi-process coordination are not supported.
- Thumbnails, advanced duplicate review, revision browsing, and metadata commits are out of scope for this read-only milestone.
