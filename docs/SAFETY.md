# Media safety policy

The application must not transcode, recompress, re-encode, or alter image pixels, video frames, or audio streams.

## Current verified-by-design read-only boundary
- SHA-256 fingerprinting opens source files read-only and hashes original bytes.
- ExifTool is invoked with read-only arguments; this application does not expose metadata write operations.
- Filename generation creates preview paths only. A detected target collision aborts the preview and does not alter the existing target.
- SQLite stores asset records, the first imported SHA-256 fingerprint, and import operation journal state.
- Import journaling is database bookkeeping, not a media commit. A completed import only means the asset record/fingerprint were stored.
- Metadata writes, bulk edit commits, and filesystem renames remain disabled.

## Import recovery protocol
1. Persist a Started journal record before beginning the asset transaction.
2. In one SQLite transaction, insert/update the asset and fingerprint and mark the journal Completed.
3. If the process stops before that transaction commits, the asset transaction rolls back and the Started journal record remains.
4. On startup, change Started/Prepared operations to Interrupted; do not infer that a media modification occurred.
5. Keep catalogued assets whose source path is missing, and display Missing source file rather than silently deleting records.
6. Report import errors in the UI; retry is a new import operation.

## Required protocol before any future metadata or filename write
1. Validate source existence and record size, modification time, and current SHA-256.
2. Read metadata and persist a snapshot before mutation.
3. Insert a durable operation journal intent.
4. Prefer XMP sidecars when embedded editing risks media payload changes. Write a temporary sidecar in the same directory, flush and verify it, then atomically move only if destination does not exist.
5. For supported embedded writes, preserve original metadata and verify media essence hashes before and after. Disable embedded writes without a proven format-specific safe path.
6. Reread metadata and verify intended changes.
7. Commit revision and journal state transactionally.
8. On failure, restore prior metadata or filename only when safe; otherwise preserve original asset and journal evidence and report an actionable recovery step.

A whole-file hash may change after embedded metadata edits. Preserve the import-time hash as immutable provenance. Only use an essence fingerprint when a format-specific implementation can validate it.
