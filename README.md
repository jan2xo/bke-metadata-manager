# BKE Metadata Manager

Offline-first desktop application for cataloguing media, inspecting metadata, standardized naming previews, duplicate recognition, and safe future edits.

**Target:** MVP v0.1.0  
**Stack:** .NET 10, Avalonia UI, SQLite, ExifTool, SHA-256.

## Read-only safety status
This milestone imports and catalogs assets without modifying source media. It does not transcode, recompress, re-encode, edit metadata, bulk-commit edits, or rename files. An import only means catalog/fingerprint records were saved; it is not a committed metadata modification.

## Requirements
- .NET 10 SDK for source builds (not required for self-contained test builds)
- ExifTool installed separately for embedded metadata display. Set `EXIFTOOL_PATH` if the executable is not available as `exiftool` on PATH. ExifTool is not bundled with either platform build.

## Build and run
    dotnet restore Bke.MetadataManager.sln
    dotnet build Bke.MetadataManager.sln --configuration Release
    dotnet test Bke.MetadataManager.sln --configuration Release
    dotnet run --project src/Bke.MetadataManager/Bke.MetadataManager.csproj

A GitHub Actions workflow runs restore, Release build, and tests on feature branch pushes and pull requests.

## Implemented in this branch
- Import multiple files through a picker or operating-system drag-and-drop.
- Import folders recursively while skipping reparse points and tolerating inaccessible subfolders.
- SHA-256 fingerprints computed from read-only streams.
- SQLite persistence for stable path-derived asset IDs, original filenames, initial fingerprints, and source availability.
- Re-import detection by catalogued path and content duplicate recognition by immutable SHA-256.
- Import operation journal and startup recovery for interrupted imports and missing source files.
- Read-only ExifTool metadata viewer for capture time, creator, description, keywords, copyright, file details, and available tags.

## Repository map
- `src/Bke.MetadataManager`: Avalonia shell and services
- `tests`: unit and SQLite integration tests
- `docs/ARCHITECTURE.md`: layers, recovery behavior, duplicate semantics, and limits
- `docs/SAFETY.md`: read-only boundary and future commit protocol
- `docs/IDENTITY-MIGRATION.md`: staged migration from path-derived IDs to immutable catalog-generated IDs

## Supported desktop targets and certification gates
BKE Metadata Manager targets **macOS Apple Silicon** and **Windows 10/11 x64**.
Avalonia/.NET support is not a substitute for native operating-system validation.

| OS | Automated gate | Real desktop interaction |
|---|---|---|
| macOS arm64 | `.github/workflows/macos-runtime.yml`: build, tests, real JPEG/PNG/HEIC/MP4/MOV ExifTool reads, process launch and self-contained test publish | Finder drop, visual window rendering, dialogs and metadata viewing still need a person |
| Windows x64 | `.github/workflows/windows-runtime.yml`: build, tests, real JPEG/PNG/MP4/MOV ExifTool reads, process launch and test-only self-contained publish | File Explorer drop, dialogs, viewing and Windows HEIC (where supported) still need a person |
| Linux | `.github/workflows/dotnet.yml`: restore, build, tests | Not an MVP desktop release target |

The Windows workflow provides an **uncertified test build**, not a customer-ready installer. It does not bundle ExifTool or certify Finder/File Explorer interaction. Do not call the desktop MVP certified until actual user-interface checks pass on *both* macOS and Windows.

For Windows development, install the .NET 10 SDK and ExifTool (for example `choco install exiftool -y`), then run the build and launch commands above in PowerShell. The Windows test-only self-contained publish does not require the .NET SDK on the staff computer, but ExifTool must still be installed or configured through `EXIFTOOL_PATH` to display embedded metadata.

**Windows acceptance checklist:** launch the executable; drag one photo, multiple photos, and nested folders from File Explorer; use Windows file/folder pickers; inspect metadata using ExifTool, including Windows HEIC samples if applicable; restart and verify the catalog; reimport and confirm duplicates; verify unchanged SHA-256 hashes; validate filenames with spaces, Unicode, and case differences. Confirm that no source file is modified.

## Known limitations
- ExifTool is an external dependency and must be installed separately to display tags.
- Stable asset IDs are derived from normalized full paths; moving a file manually creates a new path identity until move tracking is implemented.
- The macOS Apple Silicon workflow exercises generated JPEG, PNG, HEIC, MP4, and MOV fixtures with ExifTool and captures desktop startup evidence. It does not replace manual Finder drag-and-drop and visual UI certification.
- Thumbnails, advanced duplicate review, revision browsing, installer creation, and all metadata write/rename workflows remain out of scope. Self-contained CI artifacts are test builds only, not production-certified packages.
- Build/test success is reported from the actual latest CI run; do not infer it from the presence of this workflow.
