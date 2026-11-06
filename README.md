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

GitHub Actions builds/tests on Linux, Windows and macOS, and publishes a self-contained Windows x64 developer build, plus a macOS Apple Silicon **BKE Media Organizer.app** in a drag-to-Applications DMG and ZIP. The macOS application is ad-hoc signed, not Apple Developer ID signed or notarized; macOS may still require first-launch approval. Real drag-and-drop and large transfer behavior need hands-on acceptance before shipping.

## Install the macOS app

1. Open the latest successful **Desktop MVP certification** workflow on the feature branch.
2. Download the artifact named `BKE-Media-Organizer-macOS-ARM64`.
3. Unzip it, double-click the `.dmg`, and drag **BKE Media Organizer.app** into **Applications**.
4. Double-click the app to open. On first launch, macOS may show an unidentified-developer warning; after that attempt, use **System Settings → Privacy & Security → Open Anyway**.

This is an actual macOS `.app` bundle with a dock icon, `Info.plist`, and executable permissions. **It is not a notarized Apple Developer ID build.** A public/staff-ready release will need Apple Developer ID signing and notarization.

To package a local macOS ARM64 publish output:

```sh
dotnet publish src/Bke.MetadataManager/Bke.MetadataManager.csproj -c Release -r osx-arm64 --self-contained true -o output
bash scripts/package-macos.sh output dist
```


## Apple-inspired glassmorphism UI

The main window uses a native-looking macOS title region, a translucent slate-glass backdrop,
frosted event entry and destination/status cards, individually highlighted warm-gold drop tiles,
an always-last `+` category tile, a live filename example, and an operational Reveal in Finder
action. Settings uses the same glass surfaces. All controls remain real Avalonia controls;
the illustration is **not** baked into the application.

On supported Windows editions, Avalonia can request Mica/AcrylicBlur. Avalonia 11 has
more limited macOS backdrop support; on macOS, the style is achieved with transparent,
layered, gradient-tinted surfaces, and the OS may not provide a native blur-behind effect.
No claim of native Apple Liquid Glass API integration is made.

### Visual acceptance

- Compare against the approved mockup: native traffic lights, centered title,
  subtle yellow accents, frosted Event / Destination strips, four tiles across at full width.
- Drag over each individual tile: only that tile gains a brighter gold edge.
- Use a smaller window: categories wrap without overlapping the Event or status bars.
- Add a category; the new tile appears before the permanent `+` tile.
- Change the Event name; example filename and destination preview update.
- Drop media and verify physical MOVE + rename, status counts and Reveal in Finder.
- Preserve collision refusal and verified-source-deletion behavior.
