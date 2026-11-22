#!/usr/bin/env bash
# Builds Lakhfana for macOS and packages it:
#   universal release binary (arm64 + x86_64) -> mac/dist/Lakhfana.app (ad-hoc signed) -> mac/dist/Lakhfana.dmg
# Usage (from anywhere):  bash mac/scripts/bundle.sh
# Needs Xcode (a universal SwiftPM build uses Xcode's build system). With only the Command Line Tools,
# build for one architecture instead:  LAKHFANA_ARCHS=arm64 bash mac/scripts/bundle.sh
# Optional: LAKHFANA_VERSION=1.2.3 (CFBundleShortVersionString), LAKHFANA_BUILD=42 (CFBundleVersion).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MAC_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$MAC_DIR"

APP_NAME="Lakhfana"
VOL_NAME="لخفنه"
RES="$MAC_DIR/Resources"
DIST="$MAC_DIR/dist"
APP="$DIST/$APP_NAME.app"
DMG="$DIST/$APP_NAME.dmg"
ARCHS="${LAKHFANA_ARCHS:-arm64 x86_64}"

for tool in swift sips iconutil codesign hdiutil lipo plutil ditto; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "error: '$tool' not found. Install Xcode (or run: xcode-select --install)." >&2
        exit 1
    fi
done
for f in "$RES/Info.plist" "$RES/ar.lproj/InfoPlist.strings" "$RES/AppIcon-1024.png"; do
    if [[ ! -f "$f" ]]; then
        echo "error: missing $f" >&2
        exit 1
    fi
done

WORK="$(mktemp -d "${TMPDIR:-/tmp}/lakhfana.XXXXXX")"
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

# ---------------------------------------------------------------- build
BUILD_ARGS=(-c release)
for a in $ARCHS; do BUILD_ARGS+=(--arch "$a"); done

echo "==> swift build ${BUILD_ARGS[*]}"
swift build "${BUILD_ARGS[@]}"
BIN_DIR="$(swift build "${BUILD_ARGS[@]}" --show-bin-path)"
BIN="$BIN_DIR/$APP_NAME"
if [[ ! -f "$BIN" ]]; then
    echo "error: built binary not found at $BIN" >&2
    exit 1
fi
lipo -info "$BIN"

# ---------------------------------------------------------------- .app
echo "==> Assembling $APP"
rm -rf "$APP" "$DMG"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/$APP_NAME"
chmod 755 "$APP/Contents/MacOS/$APP_NAME"
cp "$RES/Info.plist" "$APP/Contents/Info.plist"
cp -R "$RES/ar.lproj" "$APP/Contents/Resources/"
printf 'APPL????' > "$APP/Contents/PkgInfo"

if [[ -n "${LAKHFANA_VERSION:-}" ]]; then
    plutil -replace CFBundleShortVersionString -string "$LAKHFANA_VERSION" "$APP/Contents/Info.plist"
fi
if [[ -n "${LAKHFANA_BUILD:-}" ]]; then
    plutil -replace CFBundleVersion -string "$LAKHFANA_BUILD" "$APP/Contents/Info.plist"
fi
plutil -lint "$APP/Contents/Info.plist" "$APP/Contents/Resources/ar.lproj/InfoPlist.strings"

# SwiftPM resource bundles, if a target ever declares resources (none today).
shopt -s nullglob
for b in "$BIN_DIR"/*.bundle; do
    echo "    copying resource bundle $(basename "$b")"
    cp -R "$b" "$APP/Contents/Resources/"
done
shopt -u nullglob

# ---------------------------------------------------------------- icon
echo "==> AppIcon.icns"
ICONSET="$WORK/AppIcon.iconset"
mkdir -p "$ICONSET"
SRC_ICON="$RES/AppIcon-1024.png"
for s in 16 32 128 256 512; do
    d=$((s * 2))
    sips -z "$s" "$s" "$SRC_ICON" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    sips -z "$d" "$d" "$SRC_ICON" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

# ---------------------------------------------------------------- sign (ad-hoc)
echo "==> codesign (ad-hoc)"
xattr -cr "$APP"
codesign --force --deep --sign - --timestamp=none "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
codesign -dv "$APP" 2>&1 | sed 's/^/    /'

# ---------------------------------------------------------------- dmg
echo "==> $DMG"
STAGE="$WORK/dmg"
mkdir -p "$STAGE"
ditto "$APP" "$STAGE/$APP_NAME.app"
ln -s /Applications "$STAGE/Applications"
ok=0
for attempt in 1 2 3; do
    if hdiutil create -volname "$VOL_NAME" -srcfolder "$STAGE" -fs HFS+ \
        -format UDZO -imagekey zlib-level=9 -ov "$DMG"; then
        ok=1
        break
    fi
    echo "    hdiutil failed (attempt $attempt), retrying..." >&2
    sleep 5
done
if [[ "$ok" -ne 1 ]]; then
    echo "error: hdiutil create failed" >&2
    exit 1
fi
hdiutil verify "$DMG"

# ---------------------------------------------------------------- summary
echo "==> Done"
echo "    version: $(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist") ($(plutil -extract CFBundleVersion raw "$APP/Contents/Info.plist"))"
echo "    binary:  $(lipo -archs "$APP/Contents/MacOS/$APP_NAME")"
du -sh "$APP/Contents/MacOS/$APP_NAME" "$APP" "$DMG"
