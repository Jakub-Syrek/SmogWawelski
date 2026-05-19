using SmogWawelski.Core;

namespace SmogWawelski.Web;

/// <summary>Snapshot listy pojazdów + czas snapshotu.</summary>
public sealed class VehicleSnapshot
{
    public IReadOnlyList<TtssVehicle> Vehicles { get; init; } = Array.Empty<TtssVehicle>();
    public long Ts { get; init; }
}

/// <summary>
/// In-memory store ostatniego snapshotu. Thread-safe via volatile reference swap —
/// wszyscy klienci czytają ten sam najnowszy obiekt bez locków.
/// </summary>
public sealed class VehicleStore
{
    private VehicleSnapshot _current = new();
    public VehicleSnapshot Current => Volatile.Read(ref _current);
    public void Update(IReadOnlyList<TtssVehicle> vehicles)
        => Volatile.Write(ref _current, new VehicleSnapshot
        {
            Vehicles = vehicles,
            Ts       = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
}

/// <summary>Cache kształtów tras — ładowane raz na starcie + refresh co 24h.</summary>
public sealed class ShapesCache
{
    private IReadOnlyList<RouteShape> _routes = Array.Empty<RouteShape>();
    public IReadOnlyList<RouteShape> Routes => Volatile.Read(ref _routes);
    public void Set(IReadOnlyList<RouteShape> routes) => Volatile.Write(ref _routes, routes);
}

/// <summary>
/// Background service — odpytuje ZTP GTFS-RT co 2s i aktualizuje VehicleStore.
/// Stan velocity trzymany jest w TtssService (singleton).
/// </summary>
public sealed class VehicleRefreshService : BackgroundService
{
    private readonly TtssService _svc;
    private readonly VehicleStore _store;
    private readonly ILogger<VehicleRefreshService> _log;

    public VehicleRefreshService(TtssService svc, VehicleStore store, ILogger<VehicleRefreshService> log)
    {
        _svc = svc; _store = store; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("VehicleRefreshService started");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var vehicles = await _svc.GetTramsAsync(ct);
                _store.Update(vehicles);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Refresh failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}

/// <summary>
/// Background service — ładuje kształty tras na starcie, odświeża co 24h.
/// Pierwszy klient i tak by to wywołał — tylko że my robimy to raz, w tle.
/// </summary>
public sealed class ShapesWarmupService : BackgroundService
{
    private readonly GtfsShapeService _svc;
    private readonly ShapesCache _cache;
    private readonly ILogger<ShapesWarmupService> _log;

    public ShapesWarmupService(GtfsShapeService svc, ShapesCache cache, ILogger<ShapesWarmupService> log)
    {
        _svc = svc; _cache = cache; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var routes = await _svc.GetRoutesAsync(ct);
                _cache.Set(routes);
                _log.LogInformation("Shapes loaded: {Count} routes", routes.Count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Shapes load failed");
            }

            try { await Task.Delay(TimeSpan.FromHours(24), ct); }
            catch (OperationCanceledException) { break; }
        }
    }
}
