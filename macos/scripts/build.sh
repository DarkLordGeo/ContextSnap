#!/usr/bin/env bash
# Builds a universal (Apple silicon + Intel) ContextSnap.app and packages a ZIP and DMG in macos/dist.
# Development builds are ad-hoc signed and NOT notarized. Set MACOS_SIGNING_IDENTITY for Developer ID signing.
set -euo pipefail
VERSION="${1:-0.0.0-dev}"
cd "$(dirname "$0")/.."
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/ContextSnap"
APP=dist/ContextSnap.app
rm -rf dist
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/ContextSnap"
sed "s/__VERSION__/${VERSION}/g" Resources/Info.plist > "$APP/Contents/Info.plist"
if [[ -n "${MACOS_SIGNING_IDENTITY:-}" ]]; then
  codesign --force --options runtime --timestamp -s "$MACOS_SIGNING_IDENTITY" "$APP"
else
  codesign --force -s - "$APP"
fi
ditto -c -k --keepParent "$APP" "dist/ContextSnap-${VERSION}-macos-universal.zip"
hdiutil create -volname ContextSnap -srcfolder "$APP" -ov -format UDZO "dist/ContextSnap-${VERSION}-macos-universal.dmg" >/dev/null
echo "Built dist/ContextSnap-${VERSION}-macos-universal.zip and .dmg"
