#!/bin/bash
# Builds XingPixel.app next to this script and installs it into ~/Applications.
# Needs Xcode Command Line Tools (xcode-select --install). Nothing else.
set -euo pipefail
cd "$(dirname "$0")"

APP="XingPixel.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
swiftc -O -o "$APP/Contents/MacOS/XingPixel" XingPixel.swift -framework AppKit
cp -R sprites characters "$APP/Contents/Resources/"
cat > "$APP/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>local.xingpixel</string>
  <key>CFBundleName</key><string>XingPixel</string>
  <key>CFBundleExecutable</key><string>XingPixel</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>LSUIElement</key><true/>
  <key>NSAppleEventsUsageDescription</key><string>Синсин показывает, что играет в Spotify, и переключает треки.</string>
</dict></plist>
EOF
# Ad-hoc signature so macOS runs it and remembers the Accessibility permission.
codesign --force --sign - "$APP"

mkdir -p "$HOME/Applications"
rm -rf "$HOME/Applications/$APP"
cp -R "$APP" "$HOME/Applications/"
echo "Installed: $HOME/Applications/$APP"
echo "Run:       open \"$HOME/Applications/$APP\""
