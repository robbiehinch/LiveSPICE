#!/usr/bin/env bash
# Publish the Avalonia head and wrap it in a minimal LiveSPICE.app bundle so
# the user can drag it into /Applications and launch from Finder.
#
# Usage:
#   ./macos/build-app.sh [arm64|x64]    (default: host arch)
set -euo pipefail

cd "$(dirname "$0")/.."

ARCH="${1:-$(uname -m)}"
case "$ARCH" in
    arm64) RID="osx-arm64" ;;
    x86_64|x64) RID="osx-x64" ;;
    *) echo "Unknown arch '$ARCH'; expected arm64 or x64" >&2; exit 1 ;;
esac

PUBLISH_DIR="bin/Release/net10.0/$RID/publish"
APP_DIR="bin/Release/net10.0/$RID/LiveSPICE.app"

# The CoreAudio native dylib lives outside this project; build it first so the
# publish step picks it up via the ProjectReference + runtimes/ layout.
NATIVE_SRC="../CoreAudio/Native/src"
NATIVE_OUT="../CoreAudio/runtimes/$RID/native/libminiaudio.dylib"
if [ ! -f "$NATIVE_OUT" ]; then
    echo ">> building libminiaudio.dylib first"
    (cd "$NATIVE_SRC" && ./build.sh)
fi

echo ">> dotnet publish ($RID) self-contained"
# self-contained bundles the .NET runtime inside the .app so end users don't
# need to install .NET themselves.  The trade-off is bundle size (~80 MB).
dotnet publish -c Release -r "$RID" --self-contained true \
    -p:UseAppHost=true \
    -p:PublishSingleFile=false

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"

cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"

# The publish step produces an apphost binary named LiveSPICE.Avalonia.  Rename
# it so the Info.plist's CFBundleExecutable matches a single, recognisable
# entry-point.
mv "$APP_DIR/Contents/MacOS/LiveSPICE.Avalonia" "$APP_DIR/Contents/MacOS/LiveSPICE"
chmod +x "$APP_DIR/Contents/MacOS/LiveSPICE"

cat > "$APP_DIR/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>LiveSPICE</string>
    <key>CFBundleDisplayName</key><string>LiveSPICE</string>
    <key>CFBundleIdentifier</key><string>org.livespice.app</string>
    <key>CFBundleVersion</key><string>1.0</string>
    <key>CFBundleShortVersionString</key><string>1.0</string>
    <key>CFBundleExecutable</key><string>LiveSPICE</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSMicrophoneUsageDescription</key><string>LiveSPICE captures live audio to drive circuit simulations.</string>
    <key>CFBundleDocumentTypes</key>
    <array>
        <dict>
            <key>CFBundleTypeName</key><string>LiveSPICE Schematic</string>
            <key>CFBundleTypeRole</key><string>Editor</string>
            <key>LSItemContentTypes</key><array><string>org.livespice.schematic</string></array>
            <key>CFBundleTypeExtensions</key><array><string>schx</string></array>
        </dict>
    </array>
</dict>
</plist>
EOF

echo ">> built $APP_DIR"
echo "Drag it into /Applications, or run:"
echo "    open '$APP_DIR'"
