using System.Text.Json;
using Microsoft.AspNetCore.ResponseCompression;
using SmogWawelski.Core;
using SmogWawelski.Web;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────
// Singletony — TtssService trzyma stan velocity per pojazd, GtfsShapeService cache na dysku.
builder.Services.AddSingleton<TtssService>();
builder.Services.AddSingleton(_ =>
{
    var cacheDir = builder.Configuration["CACHE_DIR"]
                ?? Environment.GetEnvironmentVariable("CACHE_DIR")
                ?? "/tmp/smogwawelski";
    return new GtfsShapeService(cacheDir);
});
builder.Services.AddSingleton<VehicleStore>();
builder.Services.AddSingleton<ShapesCache>();
builder.Services.AddHostedService<VehicleRefreshService>();
builder.Services.AddHostedService<ShapesWarmupService>();

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = new[] { "application/json", "text/json" };
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.UseResponseCompression();
app.UseCors();

// ── Endpoints ─────────────────────────────────────────────────────

app.MapGet("/", () => Results.Ok(new
{
    name    = "SmogWawelski API",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0",
    endpoints = new[] { "/vehicles", "/shapes", "/health" },
    source  = "https://github.com/Jakub-Syrek/SmogWawelski"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));

app.MapGet("/vehicles", (VehicleStore store) =>
{
    var snap = store.Current;
    return Results.Ok(new
    {
        ts       = snap.Ts,
        count    = snap.Vehicles.Count,
        vehicles = snap.Vehicles.Select(v => new
        {
            id       = v.Id,
            name     = v.Name,
            lat      = v.Lat,
            lng      = v.Lng,
            heading  = v.Heading,
            color    = v.Color,
            vLat     = v.VLat,
            vLng     = v.VLng,
            ts       = v.Ts
        })
    });
});

app.MapGet("/shapes", (ShapesCache cache) =>
{
    var routes = cache.Routes;
    if (routes.Count == 0) return Results.Ok(Array.Empty<object>());
    return Results.Ok(routes);
});

// Railway przekazuje port w zmiennej PORT
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine($"SmogWawelski API listening on port {port}");
app.Run();
