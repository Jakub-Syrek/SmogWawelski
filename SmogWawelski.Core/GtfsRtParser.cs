using Google.Protobuf;
using SmogWawelski.Core;

namespace SmogWawelski.Core;

/// <summary>
/// Ręczny parser GTFS-RT (Google.Protobuf.CodedInputStream).
/// Skipuje wszystkie nieznane/niezgodne pola — odporny na niestandardowe feedy.
/// </summary>
public static class GtfsRtParser
{
    public static List<GtfsVehicle> Parse(byte[] data)
    {
        var result = new List<GtfsVehicle>();
        var input  = new CodedInputStream(data);

        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field    = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            if (field == 2 && wireType == WireFormat.WireType.LengthDelimited)
            {
                // FeedEntity
                var entityBytes = input.ReadBytes().ToByteArray();
                var v = ParseEntity(entityBytes);
                if (v != null) result.Add(v);
            }
            else
            {
                input.SkipLastField();
            }
        }

        return result;
    }

    private static GtfsVehicle? ParseEntity(byte[] data)
    {
        var v     = new GtfsVehicle();
        var input = new CodedInputStream(data);
        bool hasPosition = false;

        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field    = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    v.EntityId = input.ReadString();
                    break;
                case 4 when wireType == WireFormat.WireType.LengthDelimited:
                    // VehiclePosition
                    hasPosition = ParseVehiclePosition(input.ReadBytes().ToByteArray(), v);
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return hasPosition ? v : null;
    }

    private static bool ParseVehiclePosition(byte[] data, GtfsVehicle v)
    {
        var input = new CodedInputStream(data);
        bool hasPos = false;

        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field    = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    ParseTrip(input.ReadBytes().ToByteArray(), v);
                    break;
                case 2 when wireType == WireFormat.WireType.LengthDelimited:
                {
                    // ZTP Kraków: position jest w polu 2 (niestandardowo)
                    var sub = input.ReadBytes().ToByteArray();
                    bool ok = ParsePosition(sub, v);
                    if (!ok) ParseVehicleDescriptor(sub, v); // fallback: descriptor
                    else hasPos = true;
                    break;
                }
                case 3 when wireType == WireFormat.WireType.LengthDelimited:
                    if (!hasPos) hasPos = ParsePosition(input.ReadBytes().ToByteArray(), v);
                    else input.SkipLastField();
                    break;
                case 8 when wireType == WireFormat.WireType.LengthDelimited:
                    // ZTP Kraków: VehicleDescriptor jest w polu 8
                    ParseVehicleDescriptor(input.ReadBytes().ToByteArray(), v);
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return hasPos;
    }

    private static void ParseTrip(byte[] data, GtfsVehicle v)
    {
        var input = new CodedInputStream(data);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field    = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    v.TripId = input.ReadString();
                    break;
                case 5 when wireType == WireFormat.WireType.LengthDelimited:
                    v.RouteId = input.ReadString();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }
    }

    private static void ParseVehicleDescriptor(byte[] data, GtfsVehicle v)
    {
        var input = new CodedInputStream(data);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field    = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wireType == WireFormat.WireType.LengthDelimited:
                    v.VehicleId = input.ReadString();
                    break;
                case 2 when wireType == WireFormat.WireType.LengthDelimited:
                    v.Label = input.ReadString();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }
    }

    private static bool ParsePosition(byte[] data, GtfsVehicle v)
    {
        var input = new CodedInputStream(data);
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            int field    = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);

            switch (field)
            {
                case 1 when wireType == WireFormat.WireType.Fixed32:
                    v.Latitude = input.ReadFloat();
                    break;
                case 2 when wireType == WireFormat.WireType.Fixed32:
                    v.Longitude = input.ReadFloat();
                    break;
                case 3 when wireType == WireFormat.WireType.Fixed32:
                    v.Bearing = input.ReadFloat();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return v.Latitude != 0 && v.Longitude != 0;
    }
}
