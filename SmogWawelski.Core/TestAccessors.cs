// Pomocnicze klasy eksponujące internal metody dla testów.
// Kompilowane tylko gdy jest zdefiniowany symbol TEST_ACCESSORS lub zawsze (nie ma sensytywnych danych).
namespace SmogWawelski.Core;

/// <summary>Eksponuje prywatne metody TtssService dla testów jednostkowych.</summary>
public static class TtssServiceAccessor
{
    public static string NormalizeLine(string id) => TtssService.NormalizeLinePublic(id);
    public static string LineColor(string line)   => TtssService.LineColorPublic(line);
    public static string HslToHex(int h, int s, int l) => TtssService.HslToHexPublic(h, s, l);
}

/// <summary>Eksponuje prywatne metody GtfsShapeService dla testów jednostkowych.</summary>
public static class GtfsShapeServiceAccessor
{
    public static string[] SplitCsv(string line) => GtfsShapeService.SplitCsvPublic(line);
    public static int Idx(string[] hdr, string name) => GtfsShapeService.IdxPublic(hdr, name);
}
