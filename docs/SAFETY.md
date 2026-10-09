# Media safety policy

The MVP must not transcode, recompress, re-encode, or alter image pixels, video frames, or audio streams.

## Current scaffold
- SHA-256 reads files without writing.
- ExifTool adapter reads metadata only.
- Filename generation creates preview paths only.
- SQLite schema covers immutable import fingerprints, snapshots, revisions, and operation journal.
- Metadata writes and filesystem renames are not enabled until recoverable commit semantics and tests exist.

## Required commit protocol
1. Validate source existence and record size, modification time, and current SHA-256.
2. Read metadata and persist a snapshot before mutation.
3. Insert a durable operation journal intent.
4. Prefer XMP sidecars when embedded editing risks media payload changes. Write a temporary sidecar in the same directory, flush and verify it, then atomically move only if destination does not exist.
5. For supported embedded writes, preserve original metadata and verify media essence hashes before and after. Disable embedded writes without a proven format-specific safe path.
6. Reread metadata and verify intended changes.
7. Commit revision and journal state transactionally.
8. On failure, restore prior metadata or filename only when safe; otherwise preserve original asset and journal evidence and report an actionable recovery step.

A whole-file hash may change after embedded metadata edits. Preserve the import-time hash as immutable provenance. Only use an essence fingerprint when a format-specific implementation can validate it.
