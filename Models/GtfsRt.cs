// Modele wypełniane ręcznie przez GtfsRtParser — brak atrybutów proto
namespace SmogWawelski.Models;

public class GtfsVehicle
{
    public string EntityId  { get; set; } = "";
    public string TripId    { get; set; } = "";
    public string RouteId   { get; set; } = "";
    public string VehicleId { get; set; } = "";
    public string Label     { get; set; } = "";
    public float  Latitude  { get; set; }
    public float  Longitude { get; set; }
    public float  Bearing   { get; set; }
}
