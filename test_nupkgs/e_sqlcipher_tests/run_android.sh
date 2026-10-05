#!/bin/bash
set -euo pipefail

# Builds the Android head, installs it on the connected device or emulator
# and runs the checks as an instrumentation.
#
#   ./run_android.sh [expected SQLCipher version]
#
# Set ANDROID_SERIAL to pick a device when more than one is connected.

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXPECTED="${1:-}"
PACKAGE="com.sqlitepclraw.e_sqlcipher_tests"
RUNNER="e_sqlcipher_tests.TestInstrumentation"
LOG="$(mktemp -t e_sqlcipher_android.XXXXXX)"

dotnet build "$DIR/android" -c Debug -p:ExpectedSqlcipherVersion="$EXPECTED"
APK="$DIR/android/bin/Debug/net10.0-android/$PACKAGE-Signed.apk"

adb wait-for-device
echo "=== device ABI: $(adb shell getprop ro.product.cpu.abi | tr -d '\r') ==="
adb install -r "$APK"

adb shell am instrument -w "$PACKAGE/$RUNNER" > "$LOG" 2>&1 || true
cat "$LOG"
adb uninstall "$PACKAGE" > /dev/null 2>&1 || true

if grep -q "E_SQLCIPHER_TESTS: PASSED" "$LOG"; then
    exit 0
fi
echo "Android run did not pass" >&2
exit 1
