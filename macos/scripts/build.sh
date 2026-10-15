#!/usr/bin/env bash
# Builds a universal (Apple silicon + Intel) ContextSnap.app and packages a ZIP and DMG in macos/dist.
#
# Signing (see docs/RELEASING.md):
# - Default: ad-hoc signed. macOS asks users to confirm the first launch
#   (System Settings → Privacy & Security → Open Anyway).
# - MACOS_SIGNING_IDENTITY ("Developer ID Application: …" in the keychain): signed with the hardened runtime.
# - Plus NOTARY_KEY_ID, NOTARY_ISSUER_ID, and NOTARY_KEY (an App Store Connect API key, the .p8 contents):
#   the app and the DMG are notarized by Apple and stapled, so they open without warnings.
set -euo pipefail
VERSION="${1:-0.0.0-dev}"
cd "$(dirname "$0")/.."

IDENTITY="${MACOS_SIGNING_IDENTITY:-}"
NOTARIZE=false
if [[ -n "$IDENTITY" && -n "${NOTARY_KEY_ID:-}" && -n "${NOTARY_ISSUER_ID:-}" && -n "${NOTARY_KEY:-}" ]]; then
  NOTARIZE=true
  KEY_DIR="$(mktemp -d)"
  trap 'rm -rf "$KEY_DIR"' EXIT
  (umask 077 && printf '%s\n' "$NOTARY_KEY" > "$KEY_DIR/AuthKey_${NOTARY_KEY_ID}.p8")
fi

# Submits a ZIP or DMG to Apple's notary service and fails unless it is accepted.
notarize() {
  local result status id
  result="$(xcrun notarytool submit "$1" --key "$KEY_DIR/AuthKey_${NOTARY_KEY_ID}.p8" --key-id "$NOTARY_KEY_ID" \
    --issuer "$NOTARY_ISSUER_ID" --wait --timeout 30m --output-format json)" || true
  status="$(plutil -extract status raw -o - - <<< "$result" 2>/dev/null || echo "no result")"
  if [[ "$status" != "Accepted" ]]; then
    id="$(plutil -extract id raw -o - - <<< "$result" || true)"
    echo "Notarization of $1 failed: $status" >&2
    [[ -n "$id" ]] && xcrun notarytool log "$id" --key "$KEY_DIR/AuthKey_${NOTARY_KEY_ID}.p8" --key-id "$NOTARY_KEY_ID" --issuer "$NOTARY_ISSUER_ID" >&2 || true
    exit 1
  fi
  echo "Notarized $1"
}

swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/ContextSnap"
APP=dist/ContextSnap.app
ZIP="dist/ContextSnap-${VERSION}-macos-universal.zip"
DMG="dist/ContextSnap-${VERSION}-macos-universal.dmg"
rm -rf dist
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/ContextSnap"
sed "s/__VERSION__/${VERSION}/g" Resources/Info.plist > "$APP/Contents/Info.plist"

if [[ -n "$IDENTITY" ]]; then
  codesign --force --options runtime --timestamp -s "$IDENTITY" "$APP"
else
  codesign --force -s - "$APP"
fi
codesign --verify --strict --verbose=2 "$APP"

if $NOTARIZE; then
  # Notarize the app itself first so its ticket can be stapled into the app inside both packages.
  ditto -c -k --keepParent "$APP" "dist/notarize.zip"
  notarize "dist/notarize.zip"
  rm "dist/notarize.zip"
  xcrun stapler staple "$APP"
fi

ditto -c -k --keepParent "$APP" "$ZIP"
hdiutil create -volname ContextSnap -srcfolder "$APP" -ov -format UDZO "$DMG" >/dev/null

if [[ -n "$IDENTITY" ]]; then
  codesign --force --timestamp -s "$IDENTITY" "$DMG"
fi
if $NOTARIZE; then
  notarize "$DMG"
  xcrun stapler staple "$DMG"
  # The same checks Gatekeeper makes when a downloaded copy is opened.
  spctl --assess --type execute --verbose=2 "$APP"
  spctl --assess --type open --context context:primary-signature --verbose=2 "$DMG"
  echo "Built and notarized $ZIP and $DMG"
elif [[ -n "$IDENTITY" ]]; then
  echo "Built $ZIP and $DMG (Developer ID signed, NOT notarized)"
else
  echo "Built $ZIP and $DMG (ad-hoc signed, NOT notarized)"
fi
