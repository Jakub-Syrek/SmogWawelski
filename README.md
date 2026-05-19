# 🚋 SmogWawelski — MPK Kraków Live Tracker

[![CI](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/ci.yml/badge.svg)](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/ci.yml)
[![Release](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/release.yml/badge.svg)](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/release.yml)
[![Tests](https://img.shields.io/badge/tests-35%20passing-brightgreen?logo=checkmarx)](https://github.com/Jakub-Syrek/SmogWawelski/tree/main/SmogWawelski.Tests)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet?logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Android%20%7C%20Windows-0078d4?logo=android)](https://github.com/Jakub-Syrek/SmogWawelski/releases)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![GTFS-RT](https://img.shields.io/badge/data-ZTP%20Kraków%20GTFS--RT-orange?logo=googlemaps)](https://gtfs.ztp.krakow.pl)

Real-time tram tracker for Kraków (MPK) built with **.NET MAUI**. Shows live positions of all trams on a dark interactive map with **buttery-smooth motion via server-side velocity vectors + client-side dead-reckoning**, comet trails, and the full tram network drawn from GTFS data.

---

## ✨ Features

- **Live positions** — up to ~230 trams during rush hour, polled every 2 s
- **Smooth motion via dead-reckoning** — backend computes velocity vectors per vehicle (EMA-smoothed), frontend extrapolates `pos += v · Δt` every frame. No more jerky 30-second updates — trams glide continuously.
- **Smart correction blend** — when a new position arrives, it doesn't snap. The render position smoothly converges over 1.5 s via ease-out quadratic blend.
- **Dark map** — CartoDB Dark Matter tiles via Leaflet.js
- **Comet trails** — each tram leaves a glowing, fading trail in its line color (reusable polyline pool, no GC churn)
- **Permanent track network** — full Kraków tram network drawn from GTFS shapes on startup
- **Line colors** — every route has a unique color (hardcoded for lines 1–73, hash-generated for specials)
- **Direction arrows** — bearing inferred from velocity vector (more stable than raw GPS bearing) with throttled DOM writes
- **Line filter + count badges** — tap a line tile to filter the map and center
- **Center on user** — geolocation permission, blue pulsing marker, fly-to with zoom
- **Screen recording → MP4** — Windows uses `MediaComposition` (H.264, HD 720p, 10 fps), composites WebView2 swap chain with native UI chrome via SkiaSharp. Fallback to GIF if MP4 unsupported.
- **Share to Facebook** — direct intent on Android (`com.facebook.katana`), opens fb.com + file explorer on Windows
- **Viewport culling + 30 fps throttle** — animation loop skips off-screen markers and trail updates; WebView CPU stays sane on Android even at 200+ trams
- **GTFS cache** — shapes cached on disk for 3 days, instant load on restart
- **Resilient feed** — empty/error responses from GTFS-RT fall back to last good snapshot (prevents marker wipeout flicker)

---

## 📱 Platforms

| Platform | Status |
|---|---|
| Android | ✅ APK available |
| Windows | ✅ Desktop app |
| iOS | 🔧 Requires Mac + Apple Developer account |

---

## 🏗️ Architecture

```
SmogWawelski/                       ← MAUI app (UI, WebView, recording)
│  ├── MainPage.xaml(.cs)           ← Map screen + line filter + record/share UI
│  ├── ViewModels/MapViewModel.cs   ← Auto-refresh loop (2 s), filter state
│  ├── Services/ScreenCaptureService ← Composite WebView2+chrome capture, MP4/GIF pipeline
│  ├── Services/Mp4Writer.cs        ← Windows: MediaComposition H.264 encoder
│  ├── Services/GifWriter.cs        ← Pure-C# GIF89a + LZW fallback
│  └── Resources/Raw/map.html       ← Leaflet map + dead-reckoning animation loop
SmogWawelski.Core/                  ← Shared logic (no MAUI dependency)
│  ├── GtfsRtParser                 ← Binary GTFS-RT protobuf parser
│  ├── TtssService                  ← Fetches live positions, computes velocity vectors (EMA)
│  ├── GtfsShapeService             ← Downloads + caches route shapes (3-day TTL)
│  └── TtssModels                   ← TtssVehicle (with VLat/VLng/Ts for dead-reckoning)
SmogWawelski.Tests/                 ← xUnit tests (35 tests)
```

**Data flow:**
```
ZTP Kraków GTFS-RT  ──► GtfsRtParser ──► TtssService ──► MapViewModel
     .pb (protobuf)            │           │  ▲ poll 2s        │
                               ▼           ▼  │                ▼
                         velocity tracker (EMA) ──► JSON {Lat,Lng,VLat,VLng,Ts,Heading}
                                                              │
                                                              ▼
                                                   WebView (Leaflet.js)
                                                   ▼ dead-reckoning loop @30fps
                                                   pos = (Lat,Lng) + (VLat,VLng)·(now−Ts)
                                                   + smooth correction blend on new sample

ZTP Kraków GTFS static ──► GtfsShapeService ──► disk cache ──► drawRoutes() in JS
     .zip (shapes.txt)
```

### Smooth motion — how it works

The GTFS-RT feed publishes new positions every ~15–30 s. Naive rendering = trams teleport
that often. The fix is split between backend and frontend:

**Backend** ([TtssService.cs](SmogWawelski.Core/TtssService.cs)) tracks the last *moving*
position per vehicle and its timestamp. When position changes, velocity (deg/s) is recomputed
as `Δpos / Δt` with EMA smoothing (α=0.25). Polls that return the identical position keep
the previous velocity intact — only after 60 s without motion is velocity zeroed.

**Frontend** ([map.html](Resources/Raw/map.html)) runs an animation loop at 30 fps:
`predicted_pos = anchor + velocity · (now − anchor_time)`. When a new server sample arrives,
the offset between currently rendered and newly predicted position is faded out over 1.5 s
via ease-out quadratic, so the eye never sees a snap.

---

## 🚀 Building

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- MAUI workloads: `dotnet workload install maui`

### Windows desktop
```bash
dotnet run --project SmogWawelski.csproj -f net10.0-windows10.0.19041.0
```

### Android APK
```bash
dotnet publish SmogWawelski.csproj -f net10.0-android -c Release
# APK: bin/Release/net10.0-android/publish/*-Signed.apk
```

### Tests
```bash
dotnet test SmogWawelski.Tests/
```

---

## 📡 Data Sources

| Source | URL | Update interval |
|---|---|---|
| GTFS-RT vehicles | `https://gtfs.ztp.krakow.pl/VehiclePositions_T.pb` | ~30s |
| GTFS static (shapes) | `https://gtfs.ztp.krakow.pl/GTFS_KRK_T.zip` | daily |

No API key required. Data provided by [ZTP Kraków](https://gtfs.ztp.krakow.pl).

---

## 🛠️ Tech Stack

- **.NET MAUI 10** — cross-platform UI
- **Leaflet.js 1.9** — interactive map
- **CartoDB Dark Matter** — map tiles
- **Google.Protobuf** — GTFS-RT binary parsing
- **SkiaSharp** — image composition (WebView2 + UI chrome) and GIF fallback encoder
- **Windows.Media.Editing** — H.264 MP4 encoder via `MediaComposition` (Windows)
- **xUnit** — unit tests

---

## 📂 Project Structure

```
SmogWawelski/
├── MainPage.xaml(.cs)          # Main screen: WebView + line tiles
├── AppShell.xaml               # Navigation shell
├── Resources/Raw/map.html      # Leaflet map with animations
├── Models/                     # (re-exported from Core)
├── Services/                   # (re-exported from Core)
├── ViewModels/MapViewModel.cs  # State management + auto-refresh
SmogWawelski.Core/
├── TtssService.cs              # GTFS-RT fetcher + color logic
├── GtfsRtParser.cs             # Manual protobuf parser
├── GtfsShapeService.cs         # Route shapes + disk cache
├── TtssModels.cs               # TtssVehicle model
├── GtfsRt.cs                   # GtfsVehicle model
├── TestAccessors.cs            # Internal method exposure for tests
SmogWawelski.Tests/
├── TtssServiceTests.cs         # NormalizeLine, LineColor, HslToHex
├── GtfsRtParserTests.cs        # Protobuf parsing
├── GtfsShapeServiceTests.cs    # CSV parsing, column indexing
.github/workflows/
├── ci.yml                      # Build + test on every push/PR
├── release.yml                 # Android APK on version tags
```
