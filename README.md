# SkiTelemetry

System for collecting, processing and analyzing motion telemetry from ski runs.

The project is made of several components: wearable sensors, a mobile analysis app,
a shared .NET processing library and a backend API for storing and syncing runs.

## Repository layout

| Path | Description |
| --- | --- |
| `SkiTelemetry_Core/` | Shared .NET library: models, parser, signal processing and turn detection |
| `SkiTelemetry_Analyst/` | .NET MAUI app (Android/iOS/Windows/macOS) for live data and run analysis |
| `SkiTelemetry_Core.Tests/` | Console test harness for the core algorithms |
| `SkiTelemetry_Sensor/` | ESP32 / M5StickC firmware (PlatformIO) for the motion sensors |
| `SkiTelemetry_LogConverter/` | Utility to convert raw sensor logs |
| `SkiTelemetry_Web/` | Node.js/Express API (Prisma + PostgreSQL + Cloudflare R2) deployed on Vercel |
| `SkiTelemetry_Hardware/` | 3D models and drawings (FreeCAD/DXF/STL) for the sensor mounts |

## Requirements

- .NET SDK 10
- Node.js 20+ (backend)
- PlatformIO (firmware)
- A PostgreSQL database and a Cloudflare R2 bucket (backend)

## Building the .NET solution

```bash
dotnet build SkiTelemetry.slnx
```

Run the core tests:

```bash
dotnet run --project SkiTelemetry_Core.Tests
```

The MAUI app builds for Android on Linux, and for iOS/macOS/Windows on the
respective platforms:

```bash
dotnet build SkiTelemetry_Analyst/SkiTelemetry_Analyst.csproj -f net10.0-android
```

## Backend (SkiTelemetry_Web)

```bash
cd SkiTelemetry_Web
cp .env.example .env   # then fill in the values
npm install
npx prisma migrate deploy
npm start
```

See `SkiTelemetry_Web/README.md` for the API reference.

## Sensor firmware (SkiTelemetry_Sensor)

```bash
cd SkiTelemetry_Sensor
pio run
pio run -t upload
```

## Security

Secrets are provided through environment variables (`.env`) and are never
committed. Use `SkiTelemetry_Web/.env.example` as a template. The production API
is protected with JWT access/refresh tokens; passwords are stored as bcrypt
hashes.

## License

MIT. See [LICENSE](LICENSE).
