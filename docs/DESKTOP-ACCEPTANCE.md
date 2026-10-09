# Desktop Acceptance — Read-only MVP 0.1.0

## Scope and safety

This document is for human-operated acceptance on **macOS Apple Silicon** and **Windows 10/11 x64**. CI is supporting evidence only; it does not certify native drag-and-drop, dialogs, visual metadata, or responsiveness.

**Read-only boundary:** do not edit metadata, rename/move source files, or use any write/rollback feature. Importing writes catalog records only. Do not delete an existing catalog or any source file during testing.

## Exact builds and source

Acceptance target commit (the commit used to build the supplied test artifacts):

`7cf22e0579f8f57c03340a52e0e32ec0c258c936`

- macOS ARM64 self-contained test build: artifact `bke-metadata-manager-osx-arm64-uncertified`, workflow run [37904502653](https://github.com/jan2xo/bke-metadata-manager/actions/runs/37904502653).
- Windows x64 self-contained test build: artifact `bke-metadata-manager-win-x64-uncertified`, workflow run [37904502637](https://github.com/jan2xo/bke-metadata-manager/actions/runs/37904502637).

Download the artifacts from the workflow run's **Artifacts** section. They are uncertified CI test builds, not installers. The artifact retention period is limited; if unavailable, rebuild only from the exact commit above and record the new artifact/build hash. Do not test a different commit without updating this record.

## Requirements and clean-profile method

- ExifTool must be installed separately for embedded metadata display. If it is not on PATH, set `EXIFTOOL_PATH` to the full executable path before launching the app. CI artifacts do not bundle ExifTool.
- macOS: verify the app's published executable is `Bke.MetadataManager` in the extracted `osx-arm64` folder; launch it from Finder or Terminal. If Gatekeeper warns because this is an unsigned test artifact, do not bypass organizational policy; record the warning and ask the owner.
- Windows: verify `Bke.MetadataManager.exe` in the extracted `win-x64` folder; launch normally. Do not run an executable from inside the ZIP.
- The catalog defaults to the current user's local application-data directory, under `BKE Metadata Manager/catalog.db`. On macOS this normally resolves under `~/Library/Application Support`; on Windows under the user's local application data directory (typically `%LOCALAPPDATA%\BKE Metadata Manager\catalog.db`).
- **Never delete or overwrite an existing catalog.** For a clean-profile test, use a separate OS user account/profile. Sign into that account, launch the app once, confirm the empty catalog, and use only the disposable fixture folder. Do not point the app at a production catalog. If a separate OS account is not available, stop and arrange a safe isolated test profile with the owner; do not improvise by deleting the database.

## Why the previous macOS screenshot showed missing files

The screenshot showed two catalog rows with paths beneath a temporary `/var/folders/.../T/...` directory, and both sources were reported missing. The application intentionally retains catalog records when their source path is missing; startup recovery reconciles each saved path and labels missing sources instead of deleting catalog history. This is valid recovery behavior **if** those rows already existed in the profile's catalog.

The available evidence does **not** include the runner's catalog database or a record of who created those rows. The screenshot alone cannot prove whether they were legitimate restored records or unexpected test contamination. The test integration tests use per-test temporary directories and remove those directories in cleanup; their database path is explicitly under each test's temporary root. The CI runtime fixture directory is also temporary. No evidence currently establishes that either created the two persistent default-catalog rows. Treat the origin as **unresolved**, not as a confirmed defect or confirmed contamination. The separate-OS-user clean-profile test above distinguishes a fresh launch from restoration of a prior catalog without risking existing user data.

## Reusable fixture collection

Use a dedicated folder named `BKE-Acceptance-Fixtures`; make a backup copy before testing and never edit its files after recording fingerprints. Include:

| Fixture | Expected check |
|---|---|
| `sample-exif.jpg` | ExifTool displays DateTimeOriginal `2024:01:02 03:04:05`, Artist `BKE Runtime Test`, ImageDescription `Read-only metadata fixture`, Keywords including `runtime` and `test`, Copyright `BKE test only` |
| `sample-metadata.png` | Image imports; metadata viewer shows available PNG tags (set Title to `BKE Runtime Fixture` if supported by the chosen fixture-generation tool) |
| `sample.heic` | Imports and shows available tags where the OS/ExifTool build supports HEIC |
| `sample.mp4` | Imports; available video metadata is displayed |
| `sample.mov` | Imports; available video metadata is displayed |
| `sample-exif-copy.jpg` | Byte-for-byte copy of `sample-exif.jpg` with a different filename; both entries should be identified as duplicate content |
| `space name.jpg` | Filename with spaces imports without truncation |
| `café-日本語.jpg` | Unicode filename imports and displays intact |
| `nested/level two/inside clip.mp4` | Importing the top-level folder discovers nested media |
| `corrupt.jpg` | Deliberately invalid image; app stays responsive and reports a controlled error or unavailable metadata, without crashing |

Fixture creation may use a known metadata editor before testing; after creating the fixtures, freeze them. The duplicate must be an exact copy of the original, not re-exported. On each tester's machine, record SHA-256 for every file **before** opening the app. On Windows, use `Get-FileHash -Algorithm SHA256 -LiteralPath <path>`; on macOS, use `shasum -a 256 <path>`. Save the output with the acceptance evidence. Re-run after testing and compare exact values. Do not treat hashes from independently generated files as interchangeable; both testers should use the same archived fixture pack for comparable fingerprints.

## Test record

For every item, choose **PASS**, **FAIL**, or **NOT TESTED**. Add tester name/initials, date/time and OS version, application SHA/artifact, and screenshot or log filename. A CI pass is never a human PASS.

| Gate | macOS | Windows | Expected result / evidence |
|---|---|---|---|
| App launches | NOT TESTED | NOT TESTED | Main window opens without crash; record OS/build |
| Main window rendering | NOT TESTED | NOT TESTED | Screenshot shows readable controls, list, status and metadata panel |
| Native drag-and-drop, one file | NOT TESTED | NOT TESTED | Drop a file from Finder/File Explorer; item appears |
| Native drag-and-drop, multiple files | NOT TESTED | NOT TESTED | All supported files are handled; no hang |
| Native drag-and-drop, nested folder | NOT TESTED | NOT TESTED | Drop a folder; nested media appears |
| File/folder dialogs | NOT TESTED | NOT TESTED | Open the app's file and folder picker; select fixture(s) |
| Import progress / responsiveness | NOT TESTED | NOT TESTED | Progress/status changes; window continues repainting and accepting input |
| Metadata inspection | NOT TESTED | NOT TESTED | Select JPEG and compare fields to expected values; inspect PNG/video/HEIC available tags |
| Duplicate recognition | NOT TESTED | NOT TESTED | Exact duplicate copy is marked duplicate regardless of filename |
| Restart persistence | NOT TESTED | NOT TESTED | Close normally, reopen under same test account; records and duplicate status remain |
| Missing-source recovery | NOT TESTED | NOT TESTED | In a disposable profile only, move/remove a test source outside the app, restart; record remains labelled missing; never use personal media |
| Original bytes unchanged | NOT TESTED | NOT TESTED | Before/after SHA-256 values match for every fixture |
| Spaces and Unicode paths | NOT TESTED | NOT TESTED | Names and paths remain intact; Windows case-insensitive path behavior causes no crash |
| Corrupted/unsupported media | NOT TESTED | NOT TESTED | Controlled error/unavailable metadata; app remains usable |
| No source mutation | NOT TESTED | NOT TESTED | No metadata writes, file renames, moves, or conversions occur |

### Environment and evidence

- Tester:
- Date/time and time zone:
- OS version/build:
- Device/architecture:
- App artifact name:
- App SHA-256 (hash the downloaded ZIP and, where practical, the executable):
- Source commit: `7cf22e0579f8f57c03340a52e0e32ec0c258c936`
- ExifTool version/path:
- Clean OS profile used (yes/no; do not record private account details):
- Screenshot/log filenames:
- Failures, exact reproduction steps, and recovery:

Capture screenshots of the main window, imported list, selected JPEG metadata, duplicate status, progress/status, and any failure. Avoid capturing private files or personal account details. Attach logs/screenshots to the PR or provide them to the owner.

## Current baseline status (before human testing)

- Linux CI: **PASS**.
- macOS ARM64 CI: **PASS** — 14/14 automated tests; self-contained publish and startup screenshot evidence. This does not certify Finder interaction.
- Windows x64 CI: **PASS** — 14/14 automated tests; process smoke test and self-contained publish. A nonzero main-window handle is not a visual screenshot and does not certify rendering or File Explorer interaction.
- Native Finder drag-and-drop: **NOT TESTED**.
- Native File Explorer drag-and-drop: **NOT TESTED**.
- Windows visual rendering: **NOT TESTED**.
- Human visual metadata inspection: **NOT TESTED**.
- Clean-profile first launch: **NOT TESTED**.
- Human-observed GUI responsiveness: **NOT TESTED**.

## Decision rule

Recommend PR #1 for review only after both human testers complete their platform checklists and attach evidence. Any reproducible defect blocks acceptance until fixed and the affected checks are repeated. Keep this PR unmerged and keep metadata writes and filename modification disabled.
