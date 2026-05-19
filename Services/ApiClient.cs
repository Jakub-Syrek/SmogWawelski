using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SmogWawelski.Core;

namespace SmogWawelski.Services;

/// <summary>
/// Klient HTTP do naszego backendu (SmogWawelski.Web na Railway).
/// Backend pollu je ZTP, liczy velocity i serwuje cached snapshot wszystkim klientom.
/// </summary>
public class ApiClient
{
    // Production backend on Railway.
    public const string DefaultBaseUrl = "https://smogwawelski-production.up.railway.app";

    private readonly HttpClient _http;

    public string BaseUrl { get; }

    public ApiClient(string? baseUrl = null)
    {
        BaseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SmogWawelski-MAUI/1.0");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
    }

    public string LastError { get; private set; } = "";

    public async Task<List<TtssVehicle>> GetVehiclesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<VehiclesResponse>($"{BaseUrl}/vehicles", ct);
            if (resp?.Vehicles == null) return [];

            var result = new List<TtssVehicle>(resp.Vehicles.Count);
            foreach (var v in resp.Vehicles)
            {
                result.Add(new TtssVehicle
                {
                    Id      = v.Id,
                    Name    = v.Name,
                    Lat_f   = (float)v.Lat,
                    Lng_f   = (float)v.Lng,
                    Heading = v.Heading,
                    Color   = v.Color,
                    VLat    = v.VLat,
                    VLng    = v.VLng,
                    Ts      = v.Ts
                });
            }
            LastError = "";
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[API] GetVehicles: {ex.Message}");
            return [];
        }
    }

    public async Task<List<RouteShape>> GetShapesAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<List<RouteShape>>($"{BaseUrl}/shapes", ct);
            return resp ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] GetShapes: {ex.Message}");
            return [];
        }
    }

    private sealed class VehiclesResponse
    {
        [JsonPropertyName("ts")]       public long Ts { get; set; }
        [JsonPropertyName("count")]    public int Count { get; set; }
        [JsonPropertyName("vehicles")] public List<VehicleDto> Vehicles { get; set; } = [];
    }

    private sealed class VehicleDto
    {
        [JsonPropertyName("id")]      public string Id { get; set; } = "";
        [JsonPropertyName("name")]    public string Name { get; set; } = "";
        [JsonPropertyName("lat")]     public double Lat { get; set; }
        [JsonPropertyName("lng")]     public double Lng { get; set; }
        [JsonPropertyName("heading")] public int Heading { get; set; }
        [JsonPropertyName("color")]   public string Color { get; set; } = "#E20D19";
        [JsonPropertyName("vLat")]    public double VLat { get; set; }
        [JsonPropertyName("vLng")]    public double VLng { get; set; }
        [JsonPropertyName("ts")]      public long Ts { get; set; }
    }
}
