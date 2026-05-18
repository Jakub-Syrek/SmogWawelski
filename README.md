# 🚋 SmogWawelski — MPK Kraków Live Tracker

[![CI](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/ci.yml/badge.svg)](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/ci.yml)
[![Release](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/release.yml/badge.svg)](https://github.com/Jakub-Syrek/SmogWawelski/actions/workflows/release.yml)
[![Tests](https://img.shields.io/badge/tests-35%20passing-brightgreen?logo=checkmarx)](https://github.com/Jakub-Syrek/SmogWawelski/tree/main/SmogWawelski.Tests)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet?logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![Platform](https://img.shields.io/badge/platform-Android%20%7C%20Windows-0078d4?logo=android)](https://github.com/Jakub-Syrek/SmogWawelski/releases)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![GTFS-RT](https://img.shields.io/badge/data-ZTP%20Kraków%20GTFS--RT-orange?logo=googlemaps)](https://gtfs.ztp.krakow.pl)

Real-time tram tracker for Kraków (MPK) built with **.NET MAUI**. Shows live positions of all trams on a dark interactive map with smooth animations, comet trails, and the full tram network drawn from GTFS data.

---

## ✨ Features

- **Live positions** — 80–100 trams updated every 5 seconds
- **Dark map** — CartoDB Dark Matter tiles via Leaflet.js
- **Comet trails** — each tram leaves a glowing, fading trail in its line color
- **Permanent track network** — full Kraków tram network drawn from GTFS shapes on startup
- **Line colors** — every route has a unique color (hardcoded for lines 1–73, hash-generated for specials)
- **Smooth animation** — `requestAnimationFrame` interpolation, markers never jump
- **Direction arrows** — bearing indicator on every marker
- **Line filter** — tap a line tile to filter and center the map
- **GTFS cache** — shapes cached on disk for 3 days, instant load on restart

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
SmogWawelski/          ← MAUI app (UI, WebView, Pages)
SmogWawelski.Core/     ← Shared logic (no MAUI dependency)
│  ├── GtfsRtParser    ← Binary GTFS-RT protobuf parser
│  ├── TtssService     ← Fetches live positions from ZTP Kraków
│  ├── GtfsShapeService ← Downloads + caches route shapes
│  └── Models          ← TtssVehicle, RouteShape, GtfsVehicle
SmogWawelski.Tests/    ← xUnit tests (35 tests)
```

**Data flow:**
```
ZTP Kraków GTFS-RT ──► GtfsRtParser ──► TtssService ──► MapViewModel
     .pb (protobuf)                        JSON          ──► WebView (Leaflet.js)

ZTP Kraków GTFS static ──► GtfsShapeService ──► disk cache
     .zip (shapes.txt)         ──► drawRoutes() in JS
```

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
