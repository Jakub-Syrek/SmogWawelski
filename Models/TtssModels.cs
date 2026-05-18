namespace SmogWawelski.Models;

/// <summary>
/// Pojazd z GTFS-RT (ZTP Kraków)
/// </summary>
public class TtssVehicle
{
    public string Id      { get; set; } = "";
    public string Name    { get; set; } = "";   // numer linii
    public float  Lat_f   { get; set; }         // surowe z protobuf
    public float  Lng_f   { get; set; }
    public int    Heading { get; set; }
    public string Color   { get; set; } = "#E20D19";

    // Dla serializacji JSON do JS (mapa)
    public double Lat => Lat_f;
    public double Lng => Lng_f;
}
