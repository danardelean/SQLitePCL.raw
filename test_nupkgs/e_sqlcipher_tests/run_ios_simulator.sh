#!/bin/bash
set -euo pipefail

# Builds the iOS head for the simulator, runs it and checks the result.
#
#   ./run_ios_simulator.sh [expected SQLCipher version]
#
# Uses the booted simulator, or boots the one named in IOS_SIMULATOR
# (default: the first available iPhone).

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXPECTED="${1:-}"
BUNDLE_ID="com.sqlitepclraw.e-sqlcipher-tests"
ARCH="$(uname -m)"
case "$ARCH" in
    arm64) RID="iossimulator-arm64" ;;
    *)     RID="iossimulator-x64" ;;
esac
LOG="$(mktemp -t e_sqlcipher_ios)"

dotnet build "$DIR/ios" -c Debug -r "$RID" -p:ExpectedSqlcipherVersion="$EXPECTED"
APP="$DIR/ios/bin/Debug/net10.0-ios/$RID/e_sqlcipher_tests.ios.app"

UDID="$(xcrun simctl list devices booted | grep -oE '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' | head -1 || true)"
if [ -z "$UDID" ]; then
    NAME="${IOS_SIMULATOR:-}"
    if [ -n "$NAME" ]; then
        UDID="$(xcrun simctl list devices available | grep -F "$NAME (" | grep -oE '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' | head -1 || true)"
    else
        UDID="$(xcrun simctl list devices available | grep -E '^ +iPhone' | grep -oE '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' | head -1 || true)"
    fi
    if [ -z "$UDID" ]; then
        echo "No iOS simulator available" >&2
        exit 2
    fi
    xcrun simctl boot "$UDID"
    xcrun simctl bootstatus "$UDID" -b
fi
echo "=== simulator $UDID ==="

xcrun simctl install "$UDID" "$APP"

# The app exits by itself when the checks are done; the timeout only guards against a hang.
xcrun simctl launch --console-pty "$UDID" "$BUNDLE_ID" > "$LOG" 2>&1 &
PID=$!
for _ in $(seq 1 120); do
    kill -0 "$PID" 2>/dev/null || break
    grep -q "E_SQLCIPHER_TESTS: " "$LOG" && break
    sleep 1
done
kill "$PID" 2>/dev/null || true
wait "$PID" 2>/dev/null || true
xcrun simctl terminate "$UDID" "$BUNDLE_ID" 2>/dev/null || true

cat "$LOG"
if grep -q "E_SQLCIPHER_TESTS: PASSED" "$LOG"; then
    exit 0
fi
echo "iOS simulator run did not pass" >&2
exit 1
