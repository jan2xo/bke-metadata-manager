# BKE Media Organizer (working title)

Standalone, offline-first desktop application. **Not Render Dock.** Cross-platform .NET 10 / Avalonia app.

## Workflow

1. Choose a **destination directory** under Settings.
2. Enter an **Event**, e.g. `NHLB`.
3. Drag camera media files directly onto the **Establishing**, **Detail**, **Drone**, or custom category tile. No click-to-select.
4. Files are **moved**, renamed, and entered into a per-event JSON manifest.

Example: dropping `C0123.MP4` on `Drone` for `NHLB`, captured November 6, 2026:

```text
Destination/
  NHLB/
    Drone/
      2026-11-06-C0123-NHLB-Drone.MP4
    .media-organizer.json
```

After a successful verified move, the file disappears from its original camera-dump/source plate.

## Visual

Black/near-black UI; yellow event divider and yellow accents; each category tile is an independent drop target. The `+` tile always comes last. Added categories persist to JSON.

## Safety

- Final outcome is **MOVE, not COPY**. File is staged on destination volume, SHA-256-verified against the source, promoted without overwrite, then the source is deleted. Cross-drive support requires internal staging, which may use additional I/O.
- **No overwrites.** Colliding filenames are refused; the source remains.
- Output name: `DATE-RAW-CAMERA-NAME-EVENT-CATEGORY.extension`.
- Date is sourced preferentially from embedded recording/creation metadata via managed MetadataExtractor, falling back to filesystem last-write time. The source used is recorded in the manifest.
- JSON only: settings in local user app data and `.media-organizer.json` per event. No SQLite, database server, or external ExifTool installation.
- Batch drops: one failure does not stop subsequent files. Only individual media files are accepted; folders are not recursively imported.
- If the move succeeds but the manifest cannot be updated, the app reports a moved-with-warning state.
- Media content is never re-encoded or transcoded.

## Developer commands

```sh
dotnet restore Bke.MetadataManager.slnx
dotnet build Bke.MetadataManager.slnx -c Release
dotnet test Bke.MetadataManager.slnx -c Release
dotnet run --project src/Bke.MetadataManager/Bke.MetadataManager.csproj
```

GitHub Actions builds/tests on Linux, Windows and macOS, and publishes self-contained Windows x64 and macOS ARM64 developer build artifacts. **These are not signed/notarized installers.** Real drag-and-drop and large transfer behavior need hands-on acceptance before shipping.
