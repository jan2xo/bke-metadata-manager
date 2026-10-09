# BKE Metadata Manager

Offline-first desktop application for batch media metadata management, standardized naming, duplicate prevention, and reversible edits.

**Target:** MVP v0.1.0  
**Stack:** .NET 10, Avalonia UI, SQLite, ExifTool, SHA-256.

## Safety status
This branch is an initial implementation foundation, not a release-ready metadata editor. It does not transcode media. Metadata editing and filesystem renaming are intentionally not enabled until snapshot-first commit, verification, and rollback are implemented.

## Requirements
- .NET 10 SDK
- ExifTool installed locally for metadata reading (optional for build and launch)

## Build and run
    dotnet restore src/Bke.MetadataManager/Bke.MetadataManager.csproj
    dotnet build src/Bke.MetadataManager/Bke.MetadataManager.csproj
    dotnet run --project src/Bke.MetadataManager/Bke.MetadataManager.csproj

Initialize a local catalog in application code with SqliteCatalog(path).InitializeAsync(). Read metadata with ExifToolMetadataReader().ReadAsync(path).

## Tests

Run the current unit tests with:

    dotnet test Bke.MetadataManager.sln

The tests cover filename generation/sanitization, collision refusal, SHA-256 equality across differently named identical files, and duplicate classifications. They have been authored but have not yet been executed in this environment.

## Repository map
- src/Bke.MetadataManager: Avalonia shell and core services
- tests: automated tests
- docs/ARCHITECTURE.md: layer boundaries and duplicate semantics
- docs/SAFETY.md: safety invariants and commit protocol

## MVP limitations
Drag-and-drop, thumbnail rendering, persistent asset catalog integration, metadata editing UI, commit engine, revision browsing, rollback, complete integration tests, and packaging are not yet implemented. A basic file/folder picker import queue with recursive SHA-256 scanning is present. The metadata adapter is read-only. Whole-file SHA-256 is immutable import provenance and must not be treated as a stable fingerprint after embedded metadata changes.
