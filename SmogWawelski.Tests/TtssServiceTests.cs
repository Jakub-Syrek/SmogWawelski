using SmogWawelski.Core;
using Xunit;

namespace SmogWawelski.Tests;

public class TtssServiceTests
{
    // ── NormalizeLine ────────────────────────────────────────────

    [Theory]
    [InlineData("route_31",  "31")]
    [InlineData("route_806", "806")]
    [InlineData("route_4",   "4")]
    [InlineData("T4",        "4")]
    [InlineData("T50",       "50")]
    [InlineData("4",         "4")]
    [InlineData("50",        "50")]
    [InlineData("",          "?")]
    public void NormalizeLine_ReturnsExpected(string input, string expected)
    {
        var result = TtssServiceAccessor.NormalizeLine(input);
        Assert.Equal(expected, result);
    }

    // ── LineColor ────────────────────────────────────────────────

    [Theory]
    [InlineData("1",   "#E20D19")]
    [InlineData("4",   "#2B4FA0")]
    [InlineData("70",  "#FF9800")]
    public void LineColor_KnownLines_ReturnHardcodedColor(string line, string expected)
    {
        var color = TtssServiceAccessor.LineColor(line);
        Assert.Equal(expected, color, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("806")]
    [InlineData("943")]
    [InlineData("958")]
    [InlineData("736")]
    public void LineColor_UnknownLines_ReturnValidHex(string line)
    {
        var color = TtssServiceAccessor.LineColor(line);
        Assert.Matches(@"^#[0-9A-Fa-f]{6}$", color);
    }

    [Fact]
    public void LineColor_DifferentUnknownLines_ReturnDifferentColors()
    {
        var colors = new[] { "806", "807", "808", "809", "942", "943" }
            .Select(TtssServiceAccessor.LineColor)
            .ToList();
        // Sprawdź że nie wszystkie są identyczne (hash działa)
        Assert.True(colors.Distinct().Count() > 2,
            "Każda linia powinna mieć unikalny kolor");
    }

    // ── HslToHex ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0,   0,   0,   "#000000")]
    [InlineData(0,   0,   100, "#FFFFFF")]
    [InlineData(0,   100, 50,  "#FF0000")]
    [InlineData(120, 100, 50,  "#00FF00")]
    [InlineData(240, 100, 50,  "#0000FF")]
    public void HslToHex_ReturnsCorrectColor(int h, int s, int l, string expected)
    {
        var result = TtssServiceAccessor.HslToHex(h, s, l);
        Assert.Equal(expected, result.ToUpperInvariant());
    }
}
