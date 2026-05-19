using SkiaSharp;
using System.Text;

namespace SmogWawelski.Services;

/// <summary>
/// Czysty C# encoder GIF89a z LZW — bez zewnętrznych zależności poza SkiaSharp (PNG decode).
/// </summary>
public static class GifWriter
{
    private const int MaxWidth = 480; // skaluj w dół dla rozsądnego rozmiaru pliku

    public static void Create(IList<string> framePaths, string outputPath, int frameDelayMs = 300)
    {
        int delayCs = Math.Max(2, frameDelayMs / 10); // centisekundy (1/100s)

        using var firstBmp = SKBitmap.Decode(framePaths[0]);
        int targetW = Math.Min(firstBmp.Width, MaxWidth);
        float ratio = (float)targetW / firstBmp.Width;
        int targetH = (int)(firstBmp.Height * ratio);
        // GIF wymaga parzystych wymiarów
        if (targetW % 2 != 0) targetW--;
        if (targetH % 2 != 0) targetH--;

        using var stream = File.Create(outputPath);

        // ── GIF89a Header ──────────────────────────────────────
        WriteStr(stream, "GIF89a");

        // Logical Screen Descriptor
        WriteShort(stream, targetW);
        WriteShort(stream, targetH);
        stream.WriteByte(0x70); // GCT size=1 (2 colors placeholder), no sort
        stream.WriteByte(0x00); // background index
        stream.WriteByte(0x00); // aspect ratio

        // Minimal global color table (2 entries)
        stream.Write([0, 0, 0, 255, 255, 255]);

        // Netscape extension – infinite loop
        stream.Write([0x21, 0xFF, 0x0B]);
        WriteStr(stream, "NETSCAPE2.0");
        stream.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        // ── Klatki ────────────────────────────────────────────
        foreach (var path in framePaths)
        {
            using var raw = SKBitmap.Decode(path);
            using var resized = raw.Resize(new SKImageInfo(targetW, targetH), SKSamplingOptions.Default);

            var (palette, indexed) = Quantize(resized);

            // Graphic Control Extension
            stream.Write([0x21, 0xF9, 0x04, 0x04]);
            WriteShort(stream, delayCs);
            stream.Write([0x00, 0x00]);

            // Image Descriptor z Local Color Table (256 kolorów)
            stream.WriteByte(0x2C);
            WriteShort(stream, 0); WriteShort(stream, 0);
            WriteShort(stream, targetW); WriteShort(stream, targetH);
            stream.WriteByte(0x87); // LCT flag + 256 kolorów

            // Local Color Table
            foreach (var c in palette)
            {
                stream.WriteByte(c.Red);
                stream.WriteByte(c.Green);
                stream.WriteByte(c.Blue);
            }

            // LZW image data
            stream.WriteByte(8); // min code size
            var lzw = LzwEncode(indexed, 8);
            WriteSubBlocks(stream, lzw);
        }

        stream.WriteByte(0x3B); // Trailer
    }

    // ── Kwantyzacja: 6×6×6 = 216 kolorów + 40 szarości ────────────
    private static (SKColor[] palette, byte[] indexed) Quantize(SKBitmap bmp)
    {
        var palette = new SKColor[256];
        int p = 0;
        for (int r = 0; r < 6; r++)
        for (int g = 0; g < 6; g++)
        for (int b = 0; b < 6; b++)
            palette[p++] = new SKColor((byte)(r * 51), (byte)(g * 51), (byte)(b * 51));
        for (int i = 0; i < 40; i++)
        {
            byte v = (byte)Math.Min(255, i * 7);
            palette[p++] = new SKColor(v, v, v);
        }

        var pixels  = bmp.Pixels;
        var indexed = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c  = pixels[i];
            int ri = (c.Red   + 25) / 51; if (ri > 5) ri = 5;
            int gi = (c.Green + 25) / 51; if (gi > 5) gi = 5;
            int bi = (c.Blue  + 25) / 51; if (bi > 5) bi = 5;
            indexed[i] = (byte)(ri * 36 + gi * 6 + bi);
        }
        return (palette, indexed);
    }

    // ── GIF LZW encoder ────────────────────────────────────────────
    private static byte[] LzwEncode(byte[] pixels, int minCodeSize)
    {
        int clearCode = 1 << minCodeSize;
        int eoiCode   = clearCode + 1;

        var output = new List<byte>(pixels.Length / 2);
        var bits   = new BitWriter(output);

        // kod czyszczący na start
        bits.WriteBits(clearCode, minCodeSize + 1);

        if (pixels.Length == 0)
        {
            bits.WriteBits(eoiCode, minCodeSize + 1);
            bits.Flush();
            return [..output];
        }

        // Tablica kodów: string → int
        // Inicjalizacja: każdy bajt = jego indeks
        var table    = new Dictionary<(int prefix, byte k), int>(16384);
        int nextCode = eoiCode + 1;
        int codeSize = minCodeSize + 1;
        int maxCode  = 1 << codeSize;

        int prefix = pixels[0];

        for (int i = 1; i < pixels.Length; i++)
        {
            byte k   = pixels[i];
            var  key = (prefix, k);

            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
            }
            else
            {
                bits.WriteBits(prefix, codeSize);

                if (nextCode < 4096)
                {
                    table[key] = nextCode++;
                    if (nextCode > maxCode && codeSize < 12)
                    {
                        codeSize++;
                        maxCode = 1 << codeSize;
                    }
                }

                if (nextCode >= 4096)
                {
                    // Reset — wyślij clear code
                    bits.WriteBits(clearCode, codeSize);
                    table.Clear();
                    nextCode = eoiCode + 1;
                    codeSize = minCodeSize + 1;
                    maxCode  = 1 << codeSize;
                }

                prefix = k;
            }
        }

        bits.WriteBits(prefix, codeSize);
        bits.WriteBits(eoiCode, codeSize);
        bits.Flush();
        return [..output];
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static void WriteShort(Stream s, int v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
    }

    private static void WriteStr(Stream s, string text)
        => s.Write(Encoding.ASCII.GetBytes(text));

    private static void WriteSubBlocks(Stream s, byte[] data)
    {
        int i = 0;
        while (i < data.Length)
        {
            int len = Math.Min(255, data.Length - i);
            s.WriteByte((byte)len);
            s.Write(data, i, len);
            i += len;
        }
        s.WriteByte(0);
    }

    private sealed class BitWriter(List<byte> output)
    {
        private int _buf;
        private int _bits;

        public void WriteBits(int value, int count)
        {
            _buf  |= value << _bits;
            _bits += count;
            while (_bits >= 8)
            {
                output.Add((byte)(_buf & 0xFF));
                _buf  >>= 8;
                _bits  -= 8;
            }
        }

        public void Flush()
        {
            if (_bits > 0) output.Add((byte)(_buf & 0xFF));
            _buf = _bits = 0;
        }
    }
}
