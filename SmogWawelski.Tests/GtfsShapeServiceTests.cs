using SmogWawelski.Core;
using Xunit;

namespace SmogWawelski.Tests;

public class GtfsShapeServiceTests
{
    // ── SplitCsv ─────────────────────────────────────────────────

    [Fact]
    public void SplitCsv_SimpleLine_SplitsCorrectly()
    {
        var result = GtfsShapeServiceAccessor.SplitCsv("route_1,1,Linia 1,Tram,0,E20D19,FFFFFF");
        Assert.Equal(7, result.Length);
        Assert.Equal("route_1", result[0]);
        Assert.Equal("E20D19",  result[5]);
    }

    [Fact]
    public void SplitCsv_QuotedField_RemovesQuotes()
    {
        var result = GtfsShapeServiceAccessor.SplitCsv("\"route_1\",\"Linia 1\",E20D19");
        Assert.Equal("route_1",  result[0]);
        Assert.Equal("Linia 1",  result[1]);
        Assert.Equal("E20D19",   result[2]);
    }

    [Fact]
    public void SplitCsv_QuotedComma_TreatedAsOneField()
    {
        var result = GtfsShapeServiceAccessor.SplitCsv("a,\"b,c\",d");
        Assert.Equal(3, result.Length);
        Assert.Equal("b,c", result[1]);
    }

    // ── Idx ──────────────────────────────────────────────────────

    [Fact]
    public void Idx_ExistingColumn_ReturnsCorrectIndex()
    {
        var header = new[] { "route_id", "agency_id", "route_color" };
        Assert.Equal(0, GtfsShapeServiceAccessor.Idx(header, "route_id"));
        Assert.Equal(2, GtfsShapeServiceAccessor.Idx(header, "route_color"));
    }

    [Fact]
    public void Idx_MissingColumn_ReturnsMinus1()
    {
        var header = new[] { "route_id", "agency_id" };
        Assert.Equal(-1, GtfsShapeServiceAccessor.Idx(header, "nonexistent"));
    }

    [Fact]
    public void Idx_CaseInsensitive()
    {
        var header = new[] { "Route_ID", "SHAPE_ID" };
        Assert.Equal(0, GtfsShapeServiceAccessor.Idx(header, "route_id"));
        Assert.Equal(1, GtfsShapeServiceAccessor.Idx(header, "shape_id"));
    }

    [Fact]
    public void Idx_QuotedHeader_StripsQuotes()
    {
        var header = new[] { "\"route_id\"", "\"route_color\"" };
        Assert.Equal(0, GtfsShapeServiceAccessor.Idx(header, "route_id"));
    }
}
