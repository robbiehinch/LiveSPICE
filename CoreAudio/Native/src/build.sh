#!/usr/bin/env bash
# Build libminiaudio.dylib for macOS.  Produces a universal binary (arm64 + x64)
# and drops it into both the per-RID runtimes/ directories under CoreAudio/.
set -euo pipefail

cd "$(dirname "$0")"

OUT_NAME=libminiaudio.dylib
COREAUDIO_DIR=../..   # CoreAudio/

ARM64_DIR="$COREAUDIO_DIR/runtimes/osx-arm64/native"
X64_DIR="$COREAUDIO_DIR/runtimes/osx-x64/native"
mkdir -p "$ARM64_DIR" "$X64_DIR"

CFLAGS="-O2 -fPIC -DNDEBUG -Wall -Wno-unused-function"
LDFLAGS="-shared -dynamiclib \
    -install_name @rpath/$OUT_NAME \
    -framework CoreAudio \
    -framework AudioToolbox \
    -framework AudioUnit \
    -framework CoreFoundation \
    -framework Foundation \
    -framework AVFoundation"

build_one () {
    local arch=$1
    local out=$2
    echo ">> building $arch -> $out"
    clang -arch "$arch" $CFLAGS $LDFLAGS livespice_miniaudio.c -o "$out"
}

build_one arm64 "$ARM64_DIR/$OUT_NAME"
build_one x86_64 "$X64_DIR/$OUT_NAME"

echo "done."
otool -L "$ARM64_DIR/$OUT_NAME" | head -5
