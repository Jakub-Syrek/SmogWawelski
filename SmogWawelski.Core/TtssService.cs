using System.IO.Compression;
using SmogWawelski.Core;

namespace SmogWawelski.Core;

public class TtssService
{
    private readonly HttpClient _http;
    private const string TramFeed   = "https://gtfs.ztp.krakow.pl/VehiclePositions_T.pb";
    private const string GtfsStatic = "https://gtfs.ztp.krakow.pl/GTFS_KRK_T.zip";

    // trip_id → route_id (z trips.txt)
    private Dictionary<string, string> _tripToRoute = [];
    // route_id → route_short_name (publiczny numer linii z routes.txt — np. "route_31" -> "1")
    private Dictionary<string, string> _routeIdToShortName = [];
    private DateTime _routesCachedAt = DateTime.MinValue;

    // Stan do wyliczania prędkości per pojazd (dead-reckoning na froncie).
    // Trzymamy OSTATNIĄ ruchową pozycję — feed GTFS-RT publikuje co ~15-30s, więc
    // wiele kolejnych pollów zwraca identyczne wartości. Velocity musi przeżyć te ciche okresy.
    private sealed class VState
    {
        public double Lat, Lng;        // ostatnia pozycja, która faktycznie się zmieniła
        public long   Ts;              // timestamp (ms) kiedy ta pozycja została zarejestrowana
        public long   LastSeenTs;      // ostatni poll, niezależnie od ruchu (do TTL/cleanup)
        public double VLat, VLng;      // EMA wektora prędkości (deg/s)
    }
    private readonly Dictionary<string, VState> _vstate = new(512);

    // EMA alpha — wyższe = bardziej reaktywne, niższe = stabilniej.
    // 0.25 daje wyraźnie spokojniejszy ruch — szum GPS przestaje trząść markerem.
    private const double VelEma = 0.25;

    // Po jak długim okresie bez ruchu zerujemy prędkość (pojazd faktycznie stoi).
    private const long StaleVelocityMs = 60_000;

    public string LastError { get; private set; } = "";

    // Ostatnia udana lista — używana gdy feed zwróci pustkę (chwilowy błąd ZTP).
    // Bez tego front kasowałby wszystkie markery i tworzył je od zera kilka sekund później,
    // gubiąc historię pozycji, velocity i trail (efekt: "skoki" widoczne na mapie).
    private List<TtssVehicle> _lastGood = [];

    public TtssService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SmogWawelski/1.0");
    }

    public async Task<List<TtssVehicle>> GetTramsAsync(CancellationToken ct = default)
    {
        try
        {
            // Odśwież mapowanie trip→route raz na 24h
            if ((DateTime.Now - _routesCachedAt).TotalHours > 24)
                await RefreshTripMappingAsync(ct);

            var bytes = await _http.GetByteArrayAsync(TramFeed, ct);
            var raw   = GtfsRtParser.Parse(bytes);
            var now   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = raw
                .Where(v => v.Latitude is > 49 and < 51 && v.Longitude is > 19 and < 21) // bbox Kraków
                .Select(v =>
                {
                    var routeId = v.RouteId;
                    if (string.IsNullOrEmpty(routeId) && !string.IsNullOrEmpty(v.TripId))
                        _tripToRoute.TryGetValue(v.TripId, out routeId);

                    // Preferuj route_short_name z routes.txt (publiczny numer linii).
                    // Fallback: znormalizowane route_id (gdy mapowanie nie załadowane), potem label.
                    string? shortName = null;
                    if (!string.IsNullOrEmpty(routeId))
                        _routeIdToShortName.TryGetValue(routeId, out shortName);

                    var line = !string.IsNullOrEmpty(shortName)
                        ? shortName
                        : NormalizeLine(routeId ?? v.Label ?? "?");
                    var id   = v.EntityId.Length > 0 ? v.EntityId : v.VehicleId;

                    UpdateVelocity(id, v.Latitude, v.Longitude, now, out var vlat, out var vlng);

                    return new TtssVehicle
                    {
                        Id      = id,
                        Name    = line,
                        Lat_f   = v.Latitude,
                        Lng_f   = v.Longitude,
                        Heading = (int)v.Bearing,
                        Color   = LineColor(line),
                        VLat    = vlat,
                        VLng    = vlng,
                        Ts      = now
                    };
                })
                .Where(v => v.Name != "?")
                .OrderBy(v => v.Name.PadLeft(3, '0'))
                .ToList();

            // Feed czasem (~1 na 5 pollów) zwraca pustkę — zwróć poprzedni snapshot,
            // żeby front nie kasował markerów. _lastGood = [] tylko przy pierwszym pollu
            // lub jeśli feed faktycznie nie ma tramwajów (nocą).
            if (result.Count == 0 && _lastGood.Count > 0)
            {
                Console.WriteLine($"[GTFS-RT] Pusta odpowiedź — zwracam cached ({_lastGood.Count} tramwajów)");
                return _lastGood;
            }

            PruneVelocityState(result, now);
            _lastGood = result;

            LastError = "";
            Console.WriteLine($"[GTFS-RT] {result.Count} tramwajów (raw={raw.Count})");
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[GTFS-RT] Błąd: {ex}");
            // Również przy wyjątku — zwróć cached zamiast pustki
            return _lastGood.Count > 0 ? _lastGood : [];
        }
    }

    private async Task RefreshTripMappingAsync(CancellationToken ct)
    {
        try
        {
            Console.WriteLine("[GTFS] Pobieranie GTFS_KRK_T.zip...");
            var zipBytes = await _http.GetByteArrayAsync(GtfsStatic, ct);
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

            // routes.txt → route_id → route_short_name (publiczny numer linii)
            // ZTP używa wewnętrznych route_id jak "route_31" — public-facing line może być "1".
            var routesEntry = zip.GetEntry("routes.txt");
            if (routesEntry != null)
            {
                using var rdr = new StreamReader(routesEntry.Open());
                var hdr = (await rdr.ReadLineAsync(ct) ?? "").Split(',');
                int ridx = Array.IndexOf(hdr, "route_id");
                int snidx = Array.IndexOf(hdr, "route_short_name");
                if (ridx >= 0 && snidx >= 0)
                {
                    var rmap = new Dictionary<string, string>(64);
                    string? rl;
                    while ((rl = await rdr.ReadLineAsync(ct)) != null)
                    {
                        var p = rl.Split(',');
                        if (p.Length > Math.Max(ridx, snidx))
                            rmap[p[ridx].Trim('"')] = p[snidx].Trim('"');
                    }
                    _routeIdToShortName = rmap;
                    Console.WriteLine($"[GTFS] Załadowano {rmap.Count} mapowań route_id→short_name");
                }
            }

            // trips.txt → trip_id → route_id
            var tripsEntry = zip.GetEntry("trips.txt");
            if (tripsEntry == null) return;

            using var reader = new StreamReader(tripsEntry.Open());
            var header = (await reader.ReadLineAsync(ct) ?? "").Split(',');
            int riIdx = Array.IndexOf(header, "route_id");
            int tiIdx = Array.IndexOf(header, "trip_id");
            if (riIdx < 0 || tiIdx < 0) return;

            var map = new Dictionary<string, string>(4096);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                var parts = line.Split(',');
                if (parts.Length > Math.Max(riIdx, tiIdx))
                    map[parts[tiIdx].Trim('"')] = parts[riIdx].Trim('"');
            }

            _tripToRoute    = map;
            _routesCachedAt = DateTime.Now;
            Console.WriteLine($"[GTFS] Załadowano {map.Count} tras");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GTFS] Błąd ładowania trips.txt: {ex.Message}");
        }
    }

    /// <summary>
    /// Aktualizuje stan prędkości dla pojazdu. KLUCZOWE: trzymamy ostatnią ruchową pozycję
    /// + jej timestamp. Feed ZTP publikuje co ~15-30s, więc większość pollów zwraca identyczne
    /// wartości. W "cichym" okresie zachowujemy ostatnią prędkość — front ekstrapoluje płynnie.
    /// Gdy pojawi się nowa pozycja, liczymy velocity = (new - old) / (now - st.Ts) — dt
    /// odpowiada faktycznemu czasowi między ruchomymi snapshotami feedu.
    /// </summary>
    private void UpdateVelocity(string id, double lat, double lng, long now,
                                out double vlat, out double vlng)
    {
        if (string.IsNullOrEmpty(id)) { vlat = 0; vlng = 0; return; }

        if (!_vstate.TryGetValue(id, out var st))
        {
            _vstate[id] = new VState { Lat = lat, Lng = lng, Ts = now, LastSeenTs = now };
            vlat = 0; vlng = 0;
            return;
        }

        st.LastSeenTs = now;

        var dLat = lat - st.Lat;
        var dLng = lng - st.Lng;
        var dist2 = dLat * dLat + dLng * dLng;

        // ~7m próg ruchu w stopniach (1° szer. ≈ 111km, 1° dług. w Krakowie ≈ 71km)
        const double movedThreshold = 4.0e-9;

        if (dist2 < movedThreshold)
        {
            // Pozycja niezmieniona — zachowaj velocity bez zmian. Tramwaj jedzie dalej
            // ekstrapolowany do momentu, aż feed dostarczy nową próbkę.
            // Po długim czasie bez ruchu (StaleVelocityMs) uznajemy że faktycznie stoi.
            if (now - st.Ts > StaleVelocityMs)
            {
                st.VLat = 0;
                st.VLng = 0;
                st.Ts   = now; // resetuj punkt odniesienia
            }
        }
        else
        {
            var dt = (now - st.Ts) / 1000.0;
            if (dt < 0.1) dt = 0.1; // numeryczna higiena

            // Sanity cap — tramwaj <80 km/h ≈ 22 m/s ≈ ~0.0002 deg/s. Daję zapas do 0.0005.
            const double maxV = 0.0005;
            var instVLat = Math.Clamp(dLat / dt, -maxV, maxV);
            var instVLng = Math.Clamp(dLng / dt, -maxV, maxV);

            // EMA — wygładź skoki pojedynczych próbek GPS
            if (st.VLat == 0 && st.VLng == 0)
            {
                // pierwsza obserwacja ruchu — wstrzel jednorazowo bez EMA, inaczej tramwaj
                // by stał przez kolejne kilka próbek czekając aż EMA się "rozgrzeje"
                st.VLat = instVLat;
                st.VLng = instVLng;
            }
            else
            {
                st.VLat = st.VLat * (1 - VelEma) + instVLat * VelEma;
                st.VLng = st.VLng * (1 - VelEma) + instVLng * VelEma;
            }
            st.Lat = lat;
            st.Lng = lng;
            st.Ts  = now;
        }

        vlat = st.VLat;
        vlng = st.VLng;
    }

    /// <summary>Usuń stan pojazdów, które zniknęły z feedu lub są bardzo stare.</summary>
    private void PruneVelocityState(List<TtssVehicle> current, long now)
    {
        var alive = new HashSet<string>(current.Count);
        foreach (var v in current) alive.Add(v.Id);

        var dead = new List<string>();
        foreach (var kv in _vstate)
        {
            if (!alive.Contains(kv.Key) || now - kv.Value.LastSeenTs > 120_000)
                dead.Add(kv.Key);
        }
        foreach (var k in dead) _vstate.Remove(k);
    }

    internal static string NormalizeLinePublic(string id)       => NormalizeLine(id);
    internal static string LineColorPublic(string line)          => LineColor(line);
    internal static string HslToHexPublic(int h, int s, int l)  => HslToHex(h, s, l);

    private static string NormalizeLine(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        // ZTP format: "route_31", "route_806", "T4", "4"
        var s = id;
        if (s.StartsWith("route_", StringComparison.OrdinalIgnoreCase))
            s = s[6..];
        s = s.TrimStart('T', 't').TrimStart('0');
        return string.IsNullOrEmpty(s) ? id : s;
    }

    // Kolory zdefiniowane dla głównych linii dziennych
    private static readonly Dictionary<string, string> LineColors = new()
    {
        {"1","#E20D19"},{"2","#F05A28"},{"3","#00B140"},
        {"4","#2B4FA0"},{"5","#9B30D9"},{"6","#D4A800"},
        {"7","#1E9AD6"},{"8","#00A95C"},{"9","#C8102E"},
        {"10","#0057B8"},{"11","#E87722"},{"13","#6F2C91"},
        {"14","#009CA6"},{"15","#009F6B"},{"17","#E31837"},
        {"19","#FF6B00"},{"20","#0071BC"},{"22","#A8003C"},
        {"24","#00B140"},{"50","#FF8C00"},{"52","#E91E8C"},
        {"62","#2196F3"},{"63","#FF5722"},{"64","#4CAF50"},
        {"65","#9C27B0"},{"69","#00BCD4"},{"70","#FF9800"},
        {"71","#F44336"},{"72","#3F51B5"},{"73","#009688"},
    };

    private static string LineColor(string line)
    {
        if (LineColors.TryGetValue(line, out var c)) return c;
        // Deterministic color z hasha numeru linii — żadne dwie linie nie będą szare
        var hash = 0;
        foreach (var ch in line) hash = hash * 31 + ch;
        hash = Math.Abs(hash);
        // Paleta żywych kolorów HSL — hue z hasha, wysokie nasycenie
        var hue = (hash * 137 + 60) % 360;
        return HslToHex(hue, 75, 48);
    }

    private static string HslToHex(int h, int s, int l)
    {
        double hh = h / 360.0, ss = s / 100.0, ll = l / 100.0;
        double r, g, b;
        if (ss == 0) { r = g = b = ll; }
        else
        {
            double q = ll < 0.5 ? ll * (1 + ss) : ll + ss - ll * ss;
            double p = 2 * ll - q;
            r = Hue2Rgb(p, q, hh + 1.0/3);
            g = Hue2Rgb(p, q, hh);
            b = Hue2Rgb(p, q, hh - 1.0/3);
        }
        return $"#{(int)(r*255):X2}{(int)(g*255):X2}{(int)(b*255):X2}";
    }
    private static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0/6) return p + (q-p)*6*t;
        if (t < 1.0/2) return q;
        if (t < 2.0/3) return p + (q-p)*(2.0/3-t)*6;
        return p;
    }
}
