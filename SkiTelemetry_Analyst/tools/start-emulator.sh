#!/usr/bin/env bash
#
# Avvia l'emulatore Android per SkiTelemetry_Analyst e attende il boot.
#
# Uso:
#   ./tools/start-emulator.sh                # GUI se c'e' un display, altrimenti headless
#   ./tools/start-emulator.sh --headless     # forza modalita' senza finestra
#   ./tools/start-emulator.sh --gui          # forza finestra grafica
#   ./tools/start-emulator.sh --no-wait      # avvia e non attende il boot
#   ./tools/start-emulator.sh --avd NOME     # usa un AVD diverso (default: pixel_7_api36)
#
# Eseguibile anche da fish: ./tools/start-emulator.sh
#
set -u

# ── Ambiente (override con variabili d'ambiente gia' impostate) ──────────────
DEV_HOME="${DEV_HOME:-/home/dev}"
export DOTNET_ROOT="${DOTNET_ROOT:-$DEV_HOME/dotnet}"
export JAVA_HOME="${JAVA_HOME:-$DEV_HOME/jdk-17}"
export ANDROID_HOME="${ANDROID_HOME:-$DEV_HOME/android-sdk}"
export ANDROID_SDK_ROOT="${ANDROID_SDK_ROOT:-$ANDROID_HOME}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$DOTNET_ROOT:$JAVA_HOME/bin:$ANDROID_HOME/cmdline-tools/latest/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:/usr/bin:/bin"

AVD="pixel_7_api36"
MODE="auto"      # auto | gui | headless
WAIT=1
BOOT_TIMEOUT=600 # secondi max di attesa boot con KVM
ADB="adb -s emulator-5554"

usage() {
    sed -n '2,/^$/p' "$0" | sed 's/^# \?//'
    exit "${1:-0}"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --headless) MODE="headless"; shift ;;
        --gui) MODE="gui"; shift ;;
        --no-wait) WAIT=0; shift ;;
        --avd) AVD="$2"; shift 2 ;;
        -h|--help) usage 0 ;;
        *) echo "Opzione sconosciuta: $1" >&2; usage 1 ;;
    esac
done

if [ "$MODE" = "auto" ]; then
    if [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
        MODE="gui"
    else
        MODE="headless"
    fi
fi

# ── Pre-check ─────────────────────────────────────────────────────────────────
command -v emulator >/dev/null || { echo "ERRORE: emulator non trovato in $ANDROID_HOME/emulator" >&2; exit 1; }
avdmanager list avd 2>/dev/null | grep -q "Name: $AVD" || {
    echo "ERRORE: AVD '$AVD' non trovato. Disponibili:" >&2
    avdmanager list avd 2>/dev/null | grep "Name:" >&2
    exit 1
}

# Pulisci entry adb stale (solo loopback offline, mai device reali)
adb start-server >/dev/null 2>&1
for d in $(adb devices | awk '$2=="offline" && $1 ~ /^127\.0\.0\.1:/ {print $1}'); do
    echo "Rimuovo entry stale: $d"
    adb disconnect "$d" >/dev/null 2>&1
done

# Gia' avviato e bootato? Niente da fare.
if adb devices 2>/dev/null | grep -q "^emulator-5554[[:space:]]*device"; then
    if [ "$(adb -s emulator-5554 shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; then
        echo "Emulatore '$AVD' gia' avviato e bootato (emulator-5554)."
        exit 0
    fi
    echo "Emulatore '$AVD' in avvio..."
fi

# Rimuovi lock stale di run precedenti crashati
rm -f "$HOME/.android/avd/$AVD.avd/"*.lock 2>/dev/null

# ── Accelerazione ─────────────────────────────────────────────────────────────
EXTRA_ARGS=""
if [ -c /dev/kvm ]; then
    echo "KVM disponibile: avvio accelerato."
else
    echo "ATTENZIONE: /dev/kvm non trovato, uso emulazione software (-no-accel, molto lenta)." >&2
    EXTRA_ARGS="-no-accel"
fi

# ── Avvio ─────────────────────────────────────────────────────────────────────
LOG="/tmp/emulator-$AVD.log"
ARGS=(-avd "$AVD" -gpu swiftshader_indirect -no-audio -no-snapshot -memory 2048)
[ "$MODE" = "headless" ] && ARGS+=(-no-window)
# shellcheck disable=SC2206
[ -n "$EXTRA_ARGS" ] && ARGS+=($EXTRA_ARGS)

echo "Avvio emulatore '$AVD' in modalita' $MODE (log: $LOG)..."
if [ "$MODE" = "headless" ]; then
    unset DISPLAY
    setsid nohup emulator "${ARGS[@]}" >"$LOG" 2>&1 < /dev/null &
else
    setsid nohup emulator "${ARGS[@]}" >"$LOG" 2>&1 < /dev/null &
fi
echo "PID emulatore: $!"

[ "$WAIT" -eq 0 ] && exit 0

# ── Attesa boot ───────────────────────────────────────────────────────────────
echo "Attendo il device..."
adb wait-for-device
echo "Attendo boot completato (max ${BOOT_TIMEOUT}s)..."
deadline=$((SECONDS + BOOT_TIMEOUT))
while :; do
    if [ "$(adb -s emulator-5554 shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = "1" ]; then
        break
    fi
    if [ $SECONDS -ge $deadline ]; then
        echo "ERRORE: timeout boot. Ultime righe di log:" >&2
        tail -n 15 "$LOG" >&2
        exit 1
    fi
    sleep 5
done

echo "Boot completato: $(adb -s emulator-5554 shell getprop ro.build.version.sdk 2>/dev/null | tr -d '\r') (API), $(adb -s emulator-5554 shell wm size 2>/dev/null | tr -d '\r')"
echo "Screenshot di prova: adb -s emulator-5554 exec-out screencap -p > /tmp/shot.png"
