namespace SmogWawelski.Core;

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

    // Wektor prędkości (stopnie/sekundę) — wyliczany przez TtssService dla dead-reckoning
    public double VLat { get; set; }
    public double VLng { get; set; }

    // Czas próbki (ms od epoki) — frontend ekstrapoluje od tego momentu
    public long Ts { get; set; }

    // Dla serializacji JSON do JS (mapa)
    public double Lat => Lat_f;
    public double Lng => Lng_f;
}
