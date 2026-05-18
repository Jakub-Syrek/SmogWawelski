using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmogWawelski.Services;

/// <summary>
/// Pobiera kształty tras tramwajowych z GTFS static ZTP Kraków.
/// Cache na dysku — odczyt przy kolejnych startach bez pobierania.
/// </summary>
public class GtfsShapeService
{
    private readonly HttpClient _http;
    private const string GtfsUrl   = "https://gtfs.ztp.krakow.pl/GTFS_KRK_T.zip";
    private const string CacheFile = "gtfs_shapes_cache.json";
    private const int    MaxAgeDays = 3;   // odśwież co 3 dni
    private const int    StepPts    = 3;   // co 3. punkt — kompromis dokładność/rozmiar

    public GtfsShapeService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SmogWawelski/1.0");
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>Zwraca listę tras. Używa cache lub pobiera nowe.</summary>
    public async Task<List<RouteShape>> GetRoutesAsync(CancellationToken ct = default)
    {
        var cachePath = Path.Combine(FileSystem.AppDataDirectory, CacheFile);

        // Wczytaj cache jeśli świeży
        if (File.Exists(cachePath))
        {
            var age = DateTime.Now - File.GetLastWriteTime(cachePath);
            if (age.TotalDays < MaxAgeDays)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(cachePath, ct);
                    var cached = JsonSerializer.Deserialize<List<RouteShape>>(json);
                    if (cached?.Count > 0)
                    {
                        Console.WriteLine($"[GTFS shapes] Cache ({cached.Count} tras)");
                        return cached;
                    }
                }
                catch { /* uszkodzony cache — pobierz nowy */ }
            }
        }

        // Pobierz i sparsuj
        var routes = await DownloadAndParseAsync(ct);

        // Zapisz cache
        if (routes.Count > 0)
        {
            try
            {
                var json = JsonSerializer.Serialize(routes);
                await File.WriteAllTextAsync(cachePath, json, ct);
                Console.WriteLine($"[GTFS shapes] Zapisano {routes.Count} tras do cache");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GTFS shapes] Błąd zapisu cache: {ex.Message}");
            }
        }

        return routes;
    }

    // ── Parser ────────────────────────────────────────────────────

    private async Task<List<RouteShape>> DownloadAndParseAsync(CancellationToken ct)
    {
        Console.WriteLine("[GTFS shapes] Pobieranie GTFS_KRK_T.zip...");
        byte[] zipBytes;
        try   { zipBytes = await _http.GetByteArrayAsync(GtfsUrl, ct); }
        catch (Exception ex)
        {
            Console.WriteLine($"[GTFS shapes] Błąd pobierania: {ex.Message}");
            return [];
        }

        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

        // 1. routes.txt → route_id → {name, color}
        var routeMeta = ParseRoutes(zip);

        // 2. trips.txt → route_id → zbiór shape_id (unikalny na trasę)
        var routeShapeIds = ParseTrips(zip, routeMeta.Keys.ToHashSet());

        // 3. shapes.txt → shape_id → lista punktów
        var shapePoints = ParseShapes(zip);

        // Złóż wynik
        var result = new List<RouteShape>();
        foreach (var (routeId, meta) in routeMeta)
        {
            if (!routeShapeIds.TryGetValue(routeId, out var shapeId)) continue;
            if (!shapePoints.TryGetValue(shapeId, out var pts))       continue;

            result.Add(new RouteShape
            {
                RouteId   = routeId,
                LineName  = meta.Name,
                Color     = "#" + meta.Color,
                Points    = pts
            });
        }

        Console.WriteLine($"[GTFS shapes] Sparsowano {result.Count} tras");
        return result;
    }

    // routes.txt
    private static Dictionary<string, (string Name, string Color)> ParseRoutes(ZipArchive zip)
    {
        var dict = new Dictionary<string, (string, string)>();
        var entry = zip.GetEntry("routes.txt");
        if (entry == null) return dict;

        using var reader = new StreamReader(entry.Open());
        var hdr = reader.ReadLine()?.Split(',') ?? [];
        int ri  = Idx(hdr, "route_id");
        int rn  = Idx(hdr, "route_short_name");
        int rc  = Idx(hdr, "route_color");

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var p = SplitCsv(line);
            if (p.Length <= Math.Max(ri, Math.Max(rn, rc))) continue;
            var id    = p[ri].Trim('"');
            var name  = rn >= 0 ? p[rn].Trim('"') : id;
            var color = rc >= 0 ? p[rc].Trim('"') : "607D8B";
            if (string.IsNullOrEmpty(color)) color = "607D8B";
            dict[id] = (name, color);
        }
        return dict;
    }

    // trips.txt → route → reprezentatywny shape_id (pierwszy napotkany)
    private static Dictionary<string, string> ParseTrips(ZipArchive zip, HashSet<string> knownRoutes)
    {
        var dict  = new Dictionary<string, string>();
        var entry = zip.GetEntry("trips.txt");
        if (entry == null) return dict;

        using var reader = new StreamReader(entry.Open());
        var hdr = reader.ReadLine()?.Split(',') ?? [];
        int ri  = Idx(hdr, "route_id");
        int si  = Idx(hdr, "shape_id");
        if (ri < 0 || si < 0) return dict;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var p = SplitCsv(line);
            if (p.Length <= Math.Max(ri, si)) continue;
            var routeId = p[ri].Trim('"');
            var shapeId = p[si].Trim('"');
            if (!dict.ContainsKey(routeId) && knownRoutes.Contains(routeId))
                dict[routeId] = shapeId;
        }
        return dict;
    }

    // shapes.txt → shape_id → spróbkowane punkty
    private static Dictionary<string, List<double[]>> ParseShapes(ZipArchive zip)
    {
        var dict  = new Dictionary<string, List<(double lat, double lng, int seq)>>();
        var entry = zip.GetEntry("shapes.txt");
        if (entry == null) return [];

        using var reader = new StreamReader(entry.Open());
        var hdr  = reader.ReadLine()?.Split(',') ?? [];
        int sid  = Idx(hdr, "shape_id");
        int slat = Idx(hdr, "shape_pt_lat");
        int slng = Idx(hdr, "shape_pt_lon");
        int sseq = Idx(hdr, "shape_pt_sequence");
        if (sid < 0 || slat < 0 || slng < 0) return [];

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var p = SplitCsv(line);
            if (p.Length <= Math.Max(sid, Math.Max(slat, slng))) continue;
            var shapeId = p[sid].Trim('"');
            if (!double.TryParse(p[slat].Trim('"'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lat)) continue;
            if (!double.TryParse(p[slng].Trim('"'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double lng)) continue;
            int seq = sseq >= 0 && int.TryParse(p[sseq].Trim('"'), out int s) ? s : 0;

            if (!dict.ContainsKey(shapeId)) dict[shapeId] = [];
            dict[shapeId].Add((lat, lng, seq));
        }

        // Posortuj po sekwencji, spróbkuj co StepPts
        var result = new Dictionary<string, List<double[]>>();
        foreach (var (id, pts) in dict)
        {
            var sorted = pts.OrderBy(p => p.seq).ToList();
            var sampled = new List<double[]>();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i == 0 || i == sorted.Count - 1 || i % StepPts == 0)
                    sampled.Add([sorted[i].lat, sorted[i].lng]);
            }
            result[id] = sampled;
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static int Idx(string[] hdr, string name)
        => Array.FindIndex(hdr, h => h.Trim('"', ' ').Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string[] SplitCsv(string line)
    {
        // Prosty split — obsługuje cudzysłowy
        var parts = new List<string>();
        bool inQ = false; var cur = new System.Text.StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') { inQ = !inQ; continue; }
            if (c == ',' && !inQ) { parts.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(c);
        }
        parts.Add(cur.ToString());
        return parts.ToArray();
    }
}

public class RouteShape
{
    [JsonPropertyName("routeId")]  public string RouteId  { get; set; } = "";
    [JsonPropertyName("lineName")] public string LineName { get; set; } = "";
    [JsonPropertyName("color")]    public string Color    { get; set; } = "#607D8B";
    [JsonPropertyName("points")]   public List<double[]> Points { get; set; } = [];
}
