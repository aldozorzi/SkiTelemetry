#!/usr/bin/env bash
# Build a signed/unsigned APK for the .NET MAUI Android app over USB-test use.
# Usage (repo root): ./SkiTelemetry_Analyst/tools/build-apk.sh [Debug|Release] [net10.0-android]
#
# Examples:
#   ./SkiTelemetry_Analyst/tools/build-apk.sh            # Debug build (fast, for USB terminal testing)
#   ./SkiTelemetry_Analyst/tools/build-apk.sh Release    # Release build (for distribution)
set -euo pipefail

CONFIG="${1:-Debug}"
TFM="${2:-net10.0-android}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$ROOT_DIR/SkiTelemetry_Analyst.csproj"

if [[ ! -f "$CSPROJ" ]]; then
  echo "ERROR: project not found: $CSPROJ" >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: 'dotnet' SDK not found in PATH." >&2
  echo "Install .NET 10 SDK + MAUI workload:" >&2
  echo "  dotnet workload install maui-android" >&2
  exit 1
fi

echo "==> dotnet $(dotnet --version)"
echo "==> Building $CSPROJ"
echo "    Configuration: $CONFIG"
echo "    TargetFramework: $TFM"

# Ensure Android workload is present (warn-only, don't force install)
if ! dotnet workload list 2>/dev/null | grep -qi "maui-android"; then
  echo "WARNING: 'maui-android' workload not detected. If build fails run:" >&2
  echo "  dotnet workload install maui-android" >&2
fi

# Debug -> fast build. Release -> publish (applies trimming/linking from csproj).
if [[ "$CONFIG" == "Release" ]]; then
  dotnet publish "$CSPROJ" \
    -f "$TFM" \
    -c Release \
    -p:AndroidPackageFormat=apk
else
  dotnet build "$CSPROJ" \
    -f "$TFM" \
    -c Debug \
    -p:AndroidPackageFormat=apk \
    -p:EmbedAssembliesIntoApk=true
fi

echo ""
echo "==> APKs produced:"
find "$ROOT_DIR/bin/$CONFIG/$TFM" -maxdepth 2 -name "*.apk" -printf "  %p\n" 2>/dev/null \
  || find "$ROOT_DIR/bin/$CONFIG/$TFM" -maxdepth 2 -name "*.apk" 2>/dev/null \
  || echo "  (no APK found - check build output above)"

echo ""
  echo "Next step: ./SkiTelemetry_Analyst/tools/deploy-apk.sh"
