using Google.Protobuf;
using SmogWawelski.Core;
using Xunit;

namespace SmogWawelski.Tests;

public class GtfsRtParserTests
{
    // ── Pomocnik: buduje minimalny GTFS-RT FeedMessage w pamięci ──

    /// <summary>Tworzy binarne dane GTFS-RT z jednym pojazdem.</summary>
    private static byte[] BuildFeed(
        string entityId, string tripId, string routeId,
        float lat, float lng, float bearing)
    {
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);

        // FeedHeader (field 1, LEN)
        var header = new MemoryStream();
        var hw = new CodedOutputStream(header);
        hw.WriteTag(1, WireFormat.WireType.LengthDelimited); hw.WriteString("2.0");
        hw.WriteTag(2, WireFormat.WireType.Varint);          hw.WriteUInt32(0);
        hw.Flush();
        output.WriteTag(1, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(header.ToArray()));

        // FeedEntity (field 2, LEN)
        var entity = new MemoryStream();
        var ew = new CodedOutputStream(entity);

        // id (field 1)
        ew.WriteTag(1, WireFormat.WireType.LengthDelimited); ew.WriteString(entityId);

        // VehiclePosition (field 4, LEN)
        var vp = new MemoryStream();
        var vpw = new CodedOutputStream(vp);

        // TripDescriptor (field 1, LEN)
        var trip = new MemoryStream();
        var tw = new CodedOutputStream(trip);
        tw.WriteTag(1, WireFormat.WireType.LengthDelimited); tw.WriteString(tripId);
        tw.WriteTag(5, WireFormat.WireType.LengthDelimited); tw.WriteString(routeId);
        tw.Flush();
        vpw.WriteTag(1, WireFormat.WireType.LengthDelimited);
        vpw.WriteBytes(ByteString.CopyFrom(trip.ToArray()));

        // Position (field 2, LEN) — ZTP Kraków używa pola 2
        var pos = new MemoryStream();
        var pw = new CodedOutputStream(pos);
        pw.WriteTag(1, WireFormat.WireType.Fixed32); pw.WriteFloat(lat);
        pw.WriteTag(2, WireFormat.WireType.Fixed32); pw.WriteFloat(lng);
        pw.WriteTag(3, WireFormat.WireType.Fixed32); pw.WriteFloat(bearing);
        pw.Flush();
        vpw.WriteTag(2, WireFormat.WireType.LengthDelimited);
        vpw.WriteBytes(ByteString.CopyFrom(pos.ToArray()));

        vpw.Flush();
        ew.WriteTag(4, WireFormat.WireType.LengthDelimited);
        ew.WriteBytes(ByteString.CopyFrom(vp.ToArray()));
        ew.Flush();

        output.WriteTag(2, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(entity.ToArray()));
        output.Flush();

        return ms.ToArray();
    }

    // ── Testy ────────────────────────────────────────────────────

    [Fact]
    public void Parse_SingleVehicle_ReturnsOneResult()
    {
        var data = BuildFeed("124", "block_1_trip_1_service_1", "route_4",
                             50.0614f, 19.9366f, 90f);
        var result = GtfsRtParser.Parse(data);
        Assert.Single(result);
    }

    [Fact]
    public void Parse_SingleVehicle_CorrectEntityId()
    {
        var data = BuildFeed("124", "block_1_trip_1", "route_4",
                             50.0614f, 19.9366f, 0f);
        var v = GtfsRtParser.Parse(data)[0];
        Assert.Equal("124", v.EntityId);
    }

    [Fact]
    public void Parse_SingleVehicle_CorrectRouteId()
    {
        var data = BuildFeed("99", "trip_1", "route_31",
                             50.06f, 19.94f, 180f);
        var v = GtfsRtParser.Parse(data)[0];
        Assert.Equal("route_31", v.RouteId);
    }

    [Fact]
    public void Parse_SingleVehicle_CorrectCoordinates()
    {
        var data = BuildFeed("1", "t", "r", 50.0614f, 19.9366f, 45f);
        var v = GtfsRtParser.Parse(data)[0];
        Assert.Equal(50.0614f, v.Latitude,  precision: 3);
        Assert.Equal(19.9366f, v.Longitude, precision: 3);
        Assert.Equal(45f,      v.Bearing,   precision: 1);
    }

    [Fact]
    public void Parse_EmptyData_ReturnsEmptyList()
    {
        // Pusty/minimalny feed bez encji
        var data = Array.Empty<byte>();
        var result = GtfsRtParser.Parse(data);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_AnyCoordinates_ParserDoesNotFilter()
    {
        // Parser NIE filtruje bbox — zwraca wszystko co ma pozycję
        // Filtrowanie do obszaru Krakowa odbywa się w TtssService.GetTramsAsync
        var inKrakow  = BuildFeed("1", "t", "r", 50.06f, 19.93f, 0f);
        var inWarsaw  = BuildFeed("2", "t", "r", 52.23f, 21.01f, 0f);

        Assert.Single(GtfsRtParser.Parse(inKrakow));
        Assert.Single(GtfsRtParser.Parse(inWarsaw));
    }

    [Fact]
    public void Parse_MultipleVehicles_ReturnsAll()
    {
        using var ms = new MemoryStream();
        var output = new CodedOutputStream(ms);

        // FeedHeader
        var hdr = new MemoryStream(); var hw = new CodedOutputStream(hdr);
        hw.WriteTag(1, WireFormat.WireType.LengthDelimited); hw.WriteString("2.0"); hw.Flush();
        output.WriteTag(1, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(hdr.ToArray()));

        // 3 encje
        foreach (var (id, lat, lng) in new[] { ("1", 50.06f, 19.93f), ("2", 50.07f, 19.95f), ("3", 50.05f, 19.94f) })
        {
            var feed1 = BuildFeed(id, "trip", "route_4", lat, lng, 0f);
            // Wyodrębnij tylko entity bytes (pomiń header)
            var tmp = new CodedInputStream(feed1);
            uint tag;
            while ((tag = tmp.ReadTag()) != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) == 2)
                {
                    var bytes = tmp.ReadBytes();
                    output.WriteTag(2, WireFormat.WireType.LengthDelimited);
                    output.WriteBytes(bytes);
                } else tmp.SkipLastField();
            }
        }
        output.Flush();

        var result = GtfsRtParser.Parse(ms.ToArray());
        Assert.Equal(3, result.Count);
    }
}
