#!/usr/bin/env bash
# Install the compiled APK onto the USB-attached Android terminal via adb.
# Usage:
#   ./SkiTelemetry_Analyst/tools/deploy-apk.sh [path/to/app.apk] [--launch] [--no-launch] [--timeout SECS] [-s SERIAL]
#
# If no APK path is given, the newest *.apk under bin/*/net10.0-android/ is used
# (prefers *-Signed.apk, which is what MAUI Debug builds produce).
# Run from the repo root as ./SkiTelemetry_Analyst/tools/deploy-apk.sh,
# or from SkiTelemetry_Analyst/ as ./tools/deploy-apk.sh.
#
# Env:
#   ANDROID_SERIAL    serial of the target device (when several are attached)
#   ADB_WAIT_TIMEOUT  seconds to wait for the USB device (default: 15)
#   PACKAGE_ID        Android package id (default: com.ldazrz.skitelemetry from AndroidManifest.xml)
#   ADB               adb binary (default: adb)
#
# NOTE: this script never blocks forever. If no device shows up within the
# timeout it exits 1 with Arch-specific diagnostics (udev, lsusb, dmesg).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

ADB="${ADB:-adb}"
PACKAGE_ID="${PACKAGE_ID:-com.ldazrz.skitelemetry}"
LAUNCH=true
TIMEOUT="${ADB_WAIT_TIMEOUT:-15}"
SERIAL="${ANDROID_SERIAL:-}"

APK_ARG=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --launch) LAUNCH=true; shift ;;
    --no-launch) LAUNCH=false; shift ;;
    --timeout) TIMEOUT="${2:?missing value for --timeout}"; shift 2 ;;
    --timeout=*) TIMEOUT="${1#--timeout=}"; shift ;;
    -s|--serial) SERIAL="${2:?missing value for -s/--serial}"; shift 2 ;;
    -h|--help)
      sed -n '2,16p' "$0"; exit 0 ;;
    -*)
      echo "ERROR: unknown option: $1 (try --help)" >&2; exit 1 ;;
    *)
      if [[ -n "$APK_ARG" ]]; then
        echo "ERROR: too many positional args: $1" >&2; exit 1
      fi
      APK_ARG="$1"; shift ;;
  esac
done

if ! command -v "$ADB" >/dev/null 2>&1; then
  echo "ERROR: '$ADB' not found. On Arch: sudo pacman -S android-tools" >&2
  exit 1
fi

# --- Resolve APK ---
if [[ -n "$APK_ARG" ]]; then
  APK="$APK_ARG"
else
  # Prefer *-Signed.apk, fall back to any *.apk (newest first)
  APK="$(ls -t "$ROOT_DIR"/bin/*/net10.0-android/*-Signed.apk 2>/dev/null | head -n 1 || true)"
  if [[ -z "$APK" ]]; then
    APK="$(ls -t "$ROOT_DIR"/bin/*/net10.0-android/*.apk 2>/dev/null | head -n 1 || true)"
  fi
  # Also check publish/ subfolders (dotnet publish Release layout)
  if [[ -z "$APK" ]]; then
    APK="$(ls -t "$ROOT_DIR"/bin/*/net10.0-android/publish/*.apk 2>/dev/null | head -n 1 || true)"
  fi
fi

if [[ -z "${APK:-}" || ! -f "$APK" ]]; then
  echo "ERROR: APK not found: '${APK_ARG:-<auto-detect>}'" >&2
  echo "Build one first: ./SkiTelemetry_Analyst/tools/build-apk.sh [Debug|Release]" >&2
  exit 1
fi

echo "==> APK: $APK"
echo "==> Package: $PACKAGE_ID"

ADB_ARGS=()
if [[ -n "$SERIAL" ]]; then
  ADB_ARGS+=(-s "$SERIAL")
  echo "==> Target serial: $SERIAL (timeout ${TIMEOUT}s)"
else
  echo "==> Waiting up to ${TIMEOUT}s for USB device (adb)..."
fi

# Make sure the daemon is running; start-server returns immediately.
"$ADB" start-server >/dev/null 2>&1 || true

device_ready() {
  if [[ -n "$SERIAL" ]]; then
    [[ "$("$ADB" -s "$SERIAL" get-state 2>/dev/null || true)" == "device" ]]
  else
    # Any device in state "device" (ignores "unauthorized"/"offline"/empty).
    "$ADB" devices 2>/dev/null | tail -n +2 | grep -q $'\tdevice$'
  fi
}

# Raw "serial<TAB>state" lines (serial may be empty when list is empty).
raw_device_lines() {
  "$ADB" devices 2>/dev/null | tail -n +2 | grep -v '^[[:space:]]*$' || true
}

has_state() { # has_state <adb-state>
  raw_device_lines | grep -q $'\t'"$1"'$' || return 1
}

elapsed=0
if ! device_ready; then
  while [[ "$elapsed" -lt "$TIMEOUT" ]]; do
    sleep 1
    elapsed=$((elapsed + 1))
    if device_ready; then
      break
    fi
    if [[ "$elapsed" -lt "$TIMEOUT" ]]; then
      if has_state unauthorized; then
        printf '\r    ... %ds/%ds - device found but UNAUTHORIZED: accept the RSA prompt on the phone screen' "$elapsed" "$TIMEOUT"
      elif has_state offline; then
        printf '\r    ... %ds/%ds - device OFFLINE: restarting server may help (Ctrl-C, then adb kill-server)' "$elapsed" "$TIMEOUT"
      else
        printf '\r    ... %ds/%ds - no USB device yet (Ctrl-C to abort)' "$elapsed" "$TIMEOUT"
      fi
    fi
  done
  printf '\n'
fi

echo "==> Connected devices:"
"$ADB" devices -l || true

if ! device_ready; then
  # Targeted diagnosis: unauthorized / offline / invisible are different fixes.
  if has_state unauthorized; then
    echo "" >&2
    echo "ERROR: device is USB-visible but adb status is 'unauthorized'." >&2
    echo "The phone is waiting for YOU to accept the RSA key - nothing will proceed until then." >&2
    echo "" >&2
    echo "Do this now (realme C67 / OPPO):" >&2
    echo "  1. Look at the phone screen NOW - tap 'Allow' on 'Allow USB debugging?'" >&2
    echo "     Check 'Always allow from this computer', then re-run this script." >&2
    echo "  2. No popup? Force it:" >&2
    echo "       - unplug / replug USB, watch the phone screen" >&2
    echo "       - phone: Settings > Developer options > Revoke USB debugging authorizations > OK" >&2
    echo "       - toggle USB debugging OFF > ON, set 'USB configuration' to File transfer" >&2
    echo "       - host: adb kill-server && adb start-server && adb devices -l" >&2
    echo "  3. Server as wrong user (common on Arch): never use 'sudo adb'." >&2
    echo "       ps aux | grep -i adb   # kill strays, then as YOUR user:" >&2
    echo "       adb kill-server && adb start-server" >&2
    echo "       ls -l ~/.android/adbkey*  # must be owned by you, not root" >&2
    exit 1
  fi
  if has_state offline; then
    echo "" >&2
    echo "ERROR: device shows 'offline'. Run: adb kill-server && adb start-server, re-plug, retry." >&2
    exit 1
  fi
  echo "" >&2
  echo "ERROR: no USB Android device in 'device' state after ${TIMEOUT}s." >&2
  echo "" >&2
  echo "Diagnose on Arch Linux:" >&2
  echo "  1. Terminal side: Settings > Developer options > USB debugging ON," >&2
  echo "     plug a DATA cable (charge-only cables are invisible to adb)," >&2
  echo "     accept the 'Allow USB debugging' RSA prompt (check 'Always allow')." >&2
  echo "  2. Cable/port: try another port/cable; hubs and docks often fail." >&2
  echo "     Status meanings in 'adb devices':" >&2
  echo "       empty list      = not seen at USB level (cable/udev/port)" >&2
  echo "       unauthorized    = accept the RSA prompt on the terminal screen" >&2
  echo "       offline         = run: adb kill-server && adb start-server" >&2
  echo "  3. USB visibility: run 'lsusb' - the terminal vendor must appear." >&2
  if command -v lsusb >/dev/null 2>&1; then
    echo "     --- lsusb ---" >&2
    lsusb 2>&1 | head -n 30 >&2 || true
  else
    echo "     (install with: sudo pacman -S usbutils, then run lsusb)" >&2
  fi
  echo "  4. Kernel log: run 'dmesg | tail -20' right after plugging in." >&2
  echo "     No new lines = cable/port/power issue." >&2
  echo "  5. Arch permissions (most common): install udev rules and re-plug:" >&2
  echo "       sudo pacman -S android-udev" >&2
  echo "       groups | grep -q adbusers || sudo gpasswd -a \$USER adbusers" >&2
  echo "       sudo udevadm control --reload-rules && sudo udevadm trigger" >&2
  echo "       (then log out/in, re-plug, adb kill-server && adb devices)" >&2
  echo "  6. Retry with a longer wait or a fixed serial:" >&2
  echo "       ./SkiTelemetry_Analyst/tools/deploy-apk.sh --timeout 60" >&2
  echo "       ANDROID_SERIAL=<serial> ./SkiTelemetry_Analyst/tools/deploy-apk.sh" >&2
  exit 1
fi

# With no explicit serial: if exactly one device is attached, target it
# explicitly so later adb calls don't fail with "more than one device".
if [[ -z "$SERIAL" ]]; then
  mapfile -t CANDIDATES < <("$ADB" devices 2>/dev/null | tail -n +2 | awk '$2=="device" {print $1}')
  if [[ "${#CANDIDATES[@]}" -eq 1 ]]; then
    SERIAL="${CANDIDATES[0]}"
    ADB_ARGS=(-s "$SERIAL")
    echo "==> Auto-selected target serial: $SERIAL"
  elif [[ "${#CANDIDATES[@]}" -gt 1 ]]; then
    echo "" >&2
    echo "ERROR: several devices attached (${CANDIDATES[*]}). Re-run with -s SERIAL." >&2
    exit 1
  fi
fi

STATE="$("$ADB" "${ADB_ARGS[@]}" get-state 2>&1 || true)"
echo "==> Device state: $STATE"

echo "==> Installing (adb install -r)..."
"$ADB" "${ADB_ARGS[@]}" install -r "$APK"

echo "==> Installed OK."

if [[ "$LAUNCH" == "true" ]]; then
  echo "==> Launching $PACKAGE_ID ..."
  # monkey is resilient to MainActivity Java-name mangling in MAUI apps
  "$ADB" "${ADB_ARGS[@]}" shell monkey -p "$PACKAGE_ID" -c android.intent.category.LAUNCHER 1 || true
fi

echo "Done. To watch logs: adb ${ADB_ARGS[*]:-} logcat --pid=\$(adb ${ADB_ARGS[*]:-} shell pidof $PACKAGE_ID)"
