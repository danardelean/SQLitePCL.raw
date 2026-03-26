#!/bin/bash
set -euo pipefail

# =============================================================================
# Build script for SQLCipher native libraries (cross-platform)
#
# Usage: ./build_sqlcipher.sh [--platform <platform>] [--arch <arch>]
#
# Platforms: linux, macos, windows (via cross-compile)
# Architectures: x64, arm64
#
# Prerequisites:
#   - gcc/clang toolchain
#   - OpenSSL dev headers (linux/windows) or CommonCrypto (macOS/iOS)
#   - For cross-compilation: appropriate cross-compiler toolchain
#
# Output: native/<rid>/libe_sqlcipher.so|dylib|dll
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SQLCIPHER_VERSION="4.14.0"
SQLCIPHER_SRC_DIR="${REPO_ROOT}/sqlcipher-src"
OUTPUT_DIR="${REPO_ROOT}/native"
OPENSSL_VERSION="3.4.1"

# Default to current platform
PLATFORM="${PLATFORM:-$(uname -s | tr '[:upper:]' '[:lower:]')}"
ARCH="${ARCH:-$(uname -m)}"

# Normalize arch names
case "$ARCH" in
    x86_64|amd64) ARCH="x64" ;;
    aarch64) ARCH="arm64" ;;
esac

# Map to .NET RID
case "$PLATFORM" in
    linux)
        case "$ARCH" in
            x64)   RID="linux-x64" ;;
            arm64) RID="linux-arm64" ;;
        esac
        LIB_EXT="so"
        CRYPTO_BACKEND="openssl"
        ;;
    darwin|macos)
        PLATFORM="macos"
        case "$ARCH" in
            x64)   RID="osx-x64" ;;
            arm64) RID="osx-arm64" ;;
        esac
        LIB_EXT="dylib"
        CRYPTO_BACKEND="commoncrypto"
        ;;
    windows|mingw*|msys*)
        PLATFORM="windows"
        case "$ARCH" in
            x64)   RID="win-x64" ;;
            arm64) RID="win-arm64" ;;
        esac
        LIB_EXT="dll"
        CRYPTO_BACKEND="openssl"
        ;;
    *)
        echo "Unsupported platform: $PLATFORM"
        exit 1
        ;;
esac

echo "=== Building SQLCipher $SQLCIPHER_VERSION for $RID ==="
echo "  Platform: $PLATFORM"
echo "  Arch: $ARCH"
echo "  Crypto: $CRYPTO_BACKEND"
echo "  Output: $OUTPUT_DIR/$RID/libe_sqlcipher.$LIB_EXT"

# --- Clone SQLCipher source if needed ---
if [ ! -d "$SQLCIPHER_SRC_DIR" ]; then
    echo "=== Cloning SQLCipher $SQLCIPHER_VERSION ==="
    git clone --depth 1 --branch "v${SQLCIPHER_VERSION}" \
        https://github.com/sqlcipher/sqlcipher.git "$SQLCIPHER_SRC_DIR"
fi

# --- Build OpenSSL from source if needed (for linux/windows) ---
OPENSSL_PREFIX="${REPO_ROOT}/openssl-install-${RID}"
if [ "$CRYPTO_BACKEND" = "openssl" ] && [ ! -f "${OPENSSL_PREFIX}/lib/libcrypto.a" ] && [ ! -f "${OPENSSL_PREFIX}/lib64/libcrypto.a" ]; then
    echo "=== Building OpenSSL $OPENSSL_VERSION (static) for $RID ==="
    OPENSSL_SRC="${REPO_ROOT}/openssl-${OPENSSL_VERSION}"

    if [ ! -d "$OPENSSL_SRC" ]; then
        curl -sL "https://github.com/openssl/openssl/releases/download/openssl-${OPENSSL_VERSION}/openssl-${OPENSSL_VERSION}.tar.gz" \
            -o "${REPO_ROOT}/openssl.tar.gz"
        tar xzf "${REPO_ROOT}/openssl.tar.gz" -C "$REPO_ROOT"
    fi

    cd "$OPENSSL_SRC"
    make clean 2>/dev/null || true

    OPENSSL_TARGET=""
    case "$RID" in
        linux-x64)   OPENSSL_TARGET="linux-x86_64" ;;
        linux-arm64) OPENSSL_TARGET="linux-aarch64" ;;
        win-x64)     OPENSSL_TARGET="mingw64" ;;
        win-arm64)   OPENSSL_TARGET="mingw64" ;; # cross-compile later
    esac

    ./Configure "$OPENSSL_TARGET" \
        --prefix="$OPENSSL_PREFIX" \
        --openssldir="${OPENSSL_PREFIX}/ssl" \
        no-shared no-tests no-docs \
        2>&1 | tail -3

    make -j"$(nproc)" 2>&1 | tail -3
    make install_sw 2>&1 | tail -3
    cd "$REPO_ROOT"
fi

# --- Determine crypto flags ---
CRYPTO_CFLAGS=""
CRYPTO_LDFLAGS=""
CRYPTO_LIBS=""

if [ "$CRYPTO_BACKEND" = "openssl" ]; then
    OPENSSL_LIB_DIR="${OPENSSL_PREFIX}/lib"
    [ -d "${OPENSSL_PREFIX}/lib64" ] && OPENSSL_LIB_DIR="${OPENSSL_PREFIX}/lib64"

    CRYPTO_CFLAGS="-DSQLCIPHER_CRYPTO_OPENSSL -I${OPENSSL_PREFIX}/include"
    CRYPTO_LDFLAGS="-L${OPENSSL_LIB_DIR}"
    CRYPTO_LIBS="-lcrypto -lpthread -ldl"
elif [ "$CRYPTO_BACKEND" = "commoncrypto" ]; then
    CRYPTO_CFLAGS="-DSQLCIPHER_CRYPTO_CC"
    CRYPTO_LIBS="-framework Security -framework CoreFoundation"
fi

# --- Build SQLCipher ---
echo "=== Configuring SQLCipher ==="
BUILD_DIR="${REPO_ROOT}/sqlcipher-build-${RID}"
mkdir -p "$BUILD_DIR"

cd "$SQLCIPHER_SRC_DIR"
make clean 2>/dev/null || true

./configure \
    --with-tempstore=yes \
    --disable-tcl \
    --enable-shared \
    --disable-static \
    CFLAGS="-DSQLITE_HAS_CODEC \
            ${CRYPTO_CFLAGS} \
            -DSQLITE_TEMP_STORE=2 \
            -DSQLITE_THREADSAFE=1 \
            -DSQLITE_EXTRA_INIT=sqlcipher_extra_init \
            -DSQLITE_EXTRA_SHUTDOWN=sqlcipher_extra_shutdown \
            -DSQLITE_ENABLE_FTS3 \
            -DSQLITE_ENABLE_FTS3_PARENTHESIS \
            -DSQLITE_ENABLE_FTS4 \
            -DSQLITE_ENABLE_FTS5 \
            -DSQLITE_ENABLE_JSON1 \
            -DSQLITE_ENABLE_RTREE \
            -DSQLITE_ENABLE_GEOPOLY \
            -DSQLITE_ENABLE_COLUMN_METADATA \
            -DSQLITE_ENABLE_MATH_FUNCTIONS \
            -DSQLITE_ENABLE_LOAD_EXTENSION \
            -DSQLITE_ENABLE_PREUPDATE_HOOK \
            -DSQLITE_ENABLE_SESSION \
            -DSQLITE_SOUNDEX \
            -fPIC -O2" \
    LDFLAGS="${CRYPTO_LDFLAGS}" \
    LIBS="${CRYPTO_LIBS}" \
    2>&1 | tail -5

echo "=== Building SQLCipher ==="
make -j"$(nproc)" 2>&1 | tail -5

# --- Copy output ---
echo "=== Copying output ==="
mkdir -p "$OUTPUT_DIR/$RID"

if [ "$PLATFORM" = "linux" ]; then
    cp libsqlite3.so "$OUTPUT_DIR/$RID/libe_sqlcipher.so"
    strip "$OUTPUT_DIR/$RID/libe_sqlcipher.so" 2>/dev/null || true
elif [ "$PLATFORM" = "macos" ]; then
    cp libsqlite3.dylib "$OUTPUT_DIR/$RID/libe_sqlcipher.dylib"
    strip -x "$OUTPUT_DIR/$RID/libe_sqlcipher.dylib" 2>/dev/null || true
    # Fix the install name for macOS
    install_name_tool -id "@rpath/libe_sqlcipher.dylib" "$OUTPUT_DIR/$RID/libe_sqlcipher.dylib"
elif [ "$PLATFORM" = "windows" ]; then
    cp .libs/libsqlite3-0.dll "$OUTPUT_DIR/$RID/e_sqlcipher.dll" 2>/dev/null || \
    cp libsqlite3.dll "$OUTPUT_DIR/$RID/e_sqlcipher.dll" 2>/dev/null || \
    echo "WARNING: Could not find Windows DLL output"
fi

echo "=== Done: $OUTPUT_DIR/$RID/ ==="
ls -lh "$OUTPUT_DIR/$RID/"
echo ""
echo "SQLCipher version info:"
nm -D "$OUTPUT_DIR/$RID/libe_sqlcipher.${LIB_EXT}" 2>/dev/null | grep -c "sqlite3_" || true
echo " sqlite3_* symbols exported"
