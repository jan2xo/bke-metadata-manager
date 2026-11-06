#!/usr/bin/env bash
# Convert a self-contained macOS ARM64 .NET publish directory into a real macOS app.
set -euo pipefail

PUBLISH_DIR="$1"
DIST_DIR="$2"
EXE="BKE Media Organizer"
BACKEND_EXE="Bke.MetadataManager"
APP_NAME="BKE Media Organizer"
BUNDLE="$DIST_DIR/$APP_NAME.app"

if [[ "$(uname)" != "Darwin" ]]; then echo "Run on macOS" >&2; exit 1; fi
if [[ ! -f "$PUBLISH_DIR/$BACKEND_EXE" ]]; then echo "Missing published executable" >&2; exit 1; fi
mkdir -p "$DIST_DIR"
rm -rf "$BUNDLE"
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources/Backend"
# The .NET engine is shared with Windows; the macOS front-end is truly native SwiftUI.
ditto "$PUBLISH_DIR" "$BUNDLE/Contents/Resources/Backend"
chmod +x "$BUNDLE/Contents/Resources/Backend/$BACKEND_EXE"
xcrun swiftc -O -target arm64-apple-macos13.0   -sdk "$(xcrun --sdk macosx --show-sdk-path)"   -framework SwiftUI -framework AppKit -framework UniformTypeIdentifiers   macos/BKEOrganizerApp.swift -o "$BUNDLE/Contents/MacOS/$EXE"
chmod +x "$BUNDLE/Contents/MacOS/$EXE"

cat > "$BUNDLE/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.bke.mediaorganizer</string>
  <key>CFBundleName</key><string>BKE Media Organizer</string>
  <key>CFBundleDisplayName</key><string>BKE Media Organizer</string>
  <key>CFBundleExecutable</key><string>BKE Media Organizer</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSDocumentsFolderUsageDescription</key><string>Move and organize the media you drop into a chosen destination.</string>
  <key>NSRemovableVolumesUsageDescription</key><string>Move and organize media dropped from removable drives.</string>
</dict></plist>
PLIST

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
swift scripts/generate-macos-icon.swift "$WORK/master.png"
ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
while read -r size filename; do
  sips -s format png -z "$size" "$size" "$WORK/master.png" --out "$ICONSET/$filename" >/dev/null
done <<'SIZES'
16 icon_16x16.png
32 icon_16x16@2x.png
32 icon_32x32.png
64 icon_32x32@2x.png
128 icon_128x128.png
256 icon_128x128@2x.png
256 icon_256x256.png
512 icon_256x256@2x.png
512 icon_512x512.png
1024 icon_512x512@2x.png
SIZES
iconutil -c icns "$ICONSET" -o "$BUNDLE/Contents/Resources/AppIcon.icns"

# Ad-hoc signature supports local development. It is NOT Developer ID signing
# and will not bypass macOS Gatekeeper approval on downloaded app builds.
# Ad-hoc sign executable components first, then the enclosing .app to seal resources.
find "$BUNDLE/Contents/Resources/Backend" -name '*.dylib' -print0 | while IFS= read -r -d '' lib; do
  codesign --force --sign - "$lib"
done
codesign --force --sign - "$BUNDLE/Contents/Resources/Backend/$BACKEND_EXE"
codesign --force --deep --sign - "$BUNDLE"
codesign --verify --deep --strict --verbose=2 "$BUNDLE"
plutil -lint "$BUNDLE/Contents/Info.plist"
test -x "$BUNDLE/Contents/MacOS/$EXE"
test -x "$BUNDLE/Contents/Resources/Backend/$BACKEND_EXE"
test -s "$BUNDLE/Contents/Resources/AppIcon.icns"
test "$(/usr/libexec/PlistBuddy -c 'Print CFBundlePackageType' "$BUNDLE/Contents/Info.plist")" = "APPL"
file "$BUNDLE/Contents/MacOS/$EXE" | grep -q 'arm64'

# Preserve macOS bundle attributes when zipped.
ZIP="$DIST_DIR/BKE-Media-Organizer-macOS-ARM64.zip"
rm -f "$ZIP"
ditto -c -k --sequesterRsrc --keepParent "$BUNDLE" "$ZIP"

# Standard drag-to-Applications installer disk image.
DMGROOT="$WORK/installer"
mkdir -p "$DMGROOT"
ditto "$BUNDLE" "$DMGROOT/$APP_NAME.app"
ln -s /Applications "$DMGROOT/Applications"
DMG="$DIST_DIR/BKE-Media-Organizer-macOS-ARM64.dmg"
rm -f "$DMG"
hdiutil create -volname "$APP_NAME" -srcfolder "$DMGROOT" -ov -format UDZO "$DMG"
test -s "$ZIP" && test -s "$DMG"
echo "Built: $ZIP"
echo "Built: $DMG"
