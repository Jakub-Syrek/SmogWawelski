using System.IO.Compression;
using SmogWawelski.Models;

namespace SmogWawelski.Services;

public class TtssService
{
    private readonly HttpClient _http;
    private const string TramFeed   = "https://gtfs.ztp.krakow.pl/VehiclePositions_T.pb";
    private const string GtfsStatic = "https://gtfs.ztp.krakow.pl/GTFS_KRK_T.zip";

    // trip_id → route_id (numer linii)
    private Dictionary<string, string> _tripToRoute = [];
    private DateTime _routesCachedAt = DateTime.MinValue;

    public string LastError { get; private set; } = "";

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

            var result = raw
                .Where(v => v.Latitude is > 49 and < 51 && v.Longitude is > 19 and < 21) // bbox Kraków
                .Select(v =>
                {
                    var routeId = v.RouteId;
                    if (string.IsNullOrEmpty(routeId) && !string.IsNullOrEmpty(v.TripId))
                        _tripToRoute.TryGetValue(v.TripId, out routeId);

                    var line = NormalizeLine(routeId ?? v.Label ?? "?");
                    return new TtssVehicle
                    {
                        Id      = v.EntityId.Length > 0 ? v.EntityId : v.VehicleId,
                        Name    = line,
                        Lat_f   = v.Latitude,
                        Lng_f   = v.Longitude,
                        Heading = (int)v.Bearing,
                        Color   = LineColor(line)
                    };
                })
                .Where(v => v.Name != "?")
                .OrderBy(v => v.Name.PadLeft(3, '0'))
                .ToList();

            LastError = "";
            Console.WriteLine($"[GTFS-RT] {result.Count} tramwajów (raw={raw.Count})");
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[GTFS-RT] Błąd: {ex}");
            return [];
        }
    }

    private async Task RefreshTripMappingAsync(CancellationToken ct)
    {
        try
        {
            Console.WriteLine("[GTFS] Pobieranie GTFS_KRK_T.zip...");
            var zipBytes = await _http.GetByteArrayAsync(GtfsStatic, ct);
            using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

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
