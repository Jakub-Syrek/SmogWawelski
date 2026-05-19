namespace SmogWawelski.Services;

/// <summary>
/// Screenshot PNG + nagrywanie → MP4 (Windows) / GIF (fallback) zapisywany do DCIM/Camera.
/// Na Windows screenshoty komponowane: standardowy zrzut okna + osobny zrzut WebView2 wklejany
/// w miejsce mapy, bo Screenshot.CaptureAsync nie łapie WebView2 (DirectComposition swap chain).
/// </summary>
public class ScreenCaptureService
{
    private bool _recording;
    private IDispatcherTimer? _timer;
    private readonly List<string> _frames = [];
    private readonly string _cacheDir;
    private Microsoft.Maui.Controls.WebView? _mauiWebView;

    public bool   IsRecording => _recording;
    public int    FrameCount  => _frames.Count;

    public ScreenCaptureService()
    {
        _cacheDir = Path.Combine(FileSystem.CacheDirectory, "capture");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Wstrzykuje referencję do WebView mapy — używane do composite capture na Windows.</summary>
    public void SetMapView(Microsoft.Maui.Controls.WebView mapView) => _mauiWebView = mapView;

    // ── Screenshot ────────────────────────────────────────────────

    public async Task<string?> TakeScreenshotAsync()
    {
        try
        {
            var name = $"tramwaje_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var dest = GetDcimPath(name);

            if (!await CapturePngAsync(dest)) return null;

            NotifyMediaStore(dest, "image/png");
            Console.WriteLine($"[Capture] Screenshot → {dest}");
            return dest;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Capture] Screenshot error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Zapisuje PNG do podanej ścieżki. Na Windows: composite okno + WebView2 capture
    /// (bo Screenshot.CaptureAsync nie łapie WebView2). Na pozostałych — standardowy zrzut.
    /// </summary>
    private async Task<bool> CapturePngAsync(string path)
    {
#if WINDOWS
        if (await TryCompositeCaptureWindowsAsync(path)) return true;
#endif
        try
        {
            var shot = await Screenshot.CaptureAsync();
            if (shot == null) return false;
            using var s  = await shot.OpenReadAsync();
            using var fs = File.Create(path);
            await s.CopyToAsync(fs);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Capture] PNG save error: {ex.Message}");
            return false;
        }
    }

#if WINDOWS
    /// <summary>
    /// Composite: pełny zrzut okna (Screenshot.CaptureAsync — UI chrome OK, WebView area=biała)
    /// + osobny zrzut WebView2 (CapturePreviewAsync) wklejany w miejsce mapy.
    /// </summary>
    private async Task<bool> TryCompositeCaptureWindowsAsync(string path)
    {
        var webView2 = _mauiWebView?.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.WebView2;
        var core = webView2?.CoreWebView2;
        if (webView2 == null || core == null) return false;

        try
        {
            // 1) Zrzut okna — UI chrome OK, mapa biała
            var shot = await Screenshot.CaptureAsync();
            if (shot == null) return false;
            SkiaSharp.SKBitmap? fullBmp;
            using (var ss = await shot.OpenReadAsync())
                fullBmp = SkiaSharp.SKBitmap.Decode(ss);
            if (fullBmp == null) return false;

            // 2) Zrzut WebView2 jako PNG
            using var ra = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await core.CapturePreviewAsync(
                Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, ra);
            ra.Seek(0);
            SkiaSharp.SKBitmap? webBmp;
            using (var ws = ra.AsStreamForRead())
                webBmp = SkiaSharp.SKBitmap.Decode(ws);
            if (webBmp == null) { fullBmp.Dispose(); return false; }

            // 3) Pozycja WebView2 w pikselach okna
            var topLeftDip = webView2.TransformToVisual(null)
                                     .TransformPoint(new Windows.Foundation.Point(0, 0));
            var scale = webView2.XamlRoot?.RasterizationScale ?? 1.0;
            int x = (int)Math.Round(topLeftDip.X * scale);
            int y = (int)Math.Round(topLeftDip.Y * scale);
            int w = (int)Math.Round(webView2.ActualWidth  * scale);
            int h = (int)Math.Round(webView2.ActualHeight * scale);

            // Clamp do granic obrazu okna
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > fullBmp.Width)  w = fullBmp.Width  - x;
            if (y + h > fullBmp.Height) h = fullBmp.Height - y;

            if (w > 0 && h > 0)
            {
                using var canvas = new SkiaSharp.SKCanvas(fullBmp);
                canvas.DrawBitmap(webBmp, new SkiaSharp.SKRect(x, y, x + w, y + h));
                canvas.Flush();
            }

            // 4) Zapis PNG
            using (var fs = File.Create(path))
            using (var img = SkiaSharp.SKImage.FromBitmap(fullBmp))
            using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 95))
                data.SaveTo(fs);

            webBmp.Dispose();
            fullBmp.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Capture] Composite failed: {ex.Message}");
            return false;
        }
    }
#endif

    public static async Task ShareFileAsync(string path, string title)
        => await Share.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });

    // ── Recording ─────────────────────────────────────────────────

    /// <param name="fpsMs">Milliseconds between frames (250 = 4 FPS)</param>
    /// <param name="maxSeconds">Auto-stop after this many seconds</param>
    public void StartRecording(IDispatcher dispatcher, int fpsMs = 250, int maxSeconds = 120)
    {
        if (_recording) return;
        _recording = true;
        _frames.Clear();
        int elapsed = 0;

        _timer          = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(fpsMs);
        _timer.Tick    += async (_, _) =>
        {
            await CaptureFrameAsync();
            elapsed += fpsMs;
            if (elapsed >= maxSeconds * 1000)
                await MainThread.InvokeOnMainThreadAsync(() => _timer?.Stop());
        };
        _timer.Start();
        Console.WriteLine($"[Capture] Recording started ({fpsMs}ms/frame, max {maxSeconds}s)");
    }

    /// <summary>
    /// Stop nagrywania i zapis pliku wideo. Preferuje MP4 (H.264) jeśli platforma wspiera,
    /// fallback do GIF dla zachowania kompatybilności (Android — póki MediaCodec encoder
    /// nie jest gotowy).
    /// </summary>
    /// <param name="frameDelayMs">
    /// Czas trwania klatki w finalnym wideo. Dla MP4 typowo 100ms (10fps),
    /// dla GIF 200-1000ms (oszczędność rozmiaru).
    /// </param>
    public async Task<string?> StopAndSaveAsync(int frameDelayMs = 100, Action<string>? onStatus = null)
    {
        _recording = false;
        _timer?.Stop();
        _timer = null;
        await Task.Delay(300); // daj czas ostatniej klatce

        // SNAPSHOT — natychmiast wyciągnij klatki z _frames żeby encoder operował na stabilnej liście.
        // Jeśli user kliknie Record podczas encodowania (rozpoczynając nową sesję), _frames jest puste,
        // a stara sesja kończy się bezpiecznie.
        var frames = _frames.ToList();
        _frames.Clear();

        Console.WriteLine($"[Capture] Stopped — {frames.Count} frames");
        if (frames.Count < 2)
        {
            foreach (var f in frames) try { File.Delete(f); } catch { }
            return null;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string outPath;
        string mime;

        if (Mp4Writer.IsSupported)
        {
            outPath = GetDcimPath($"tramwaje_{stamp}.mp4");
            mime    = "video/mp4";
            onStatus?.Invoke($"Renderuję MP4 ({frames.Count} klatek)…");
            try
            {
                await Mp4Writer.CreateAsync(frames, outPath, frameDelayMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Capture] MP4 encode failed: {ex.Message} — fallback do GIF");
                outPath = GetDcimPath($"tramwaje_{stamp}.gif");
                mime    = "image/gif";
                await Task.Run(() => GifWriter.Create(frames, outPath, frameDelayMs: Math.Max(200, frameDelayMs)));
            }
        }
        else
        {
            outPath = GetDcimPath($"tramwaje_{stamp}.gif");
            mime    = "image/gif";
            onStatus?.Invoke($"Kompiluję GIF ({frames.Count} klatek)…");
            await Task.Run(() => GifWriter.Create(frames, outPath, frameDelayMs: Math.Max(200, frameDelayMs)));
        }

        // Wyczyść klatki tymczasowe (operuj na snapshocie — nie ruszamy bieżącego _frames jeśli user już zaczął nowe nagranie)
        foreach (var f in frames) try { File.Delete(f); } catch { }

        NotifyMediaStore(outPath, mime);
        Console.WriteLine($"[Capture] {mime} → {outPath}");
        return outPath;
    }

    // Backward-compat — stara nazwa nadal wywoływana z MainPage
    public Task<string?> StopAndSaveGifAsync(Action<string>? onStatus = null)
        => StopAndSaveAsync(frameDelayMs: 100, onStatus);

    private async Task CaptureFrameAsync()
    {
        try
        {
            var path = Path.Combine(_cacheDir, $"f_{DateTime.Now:HHmmss_fff}.png");
            if (await CapturePngAsync(path))
            {
                // Po Stop() lookg może jeszcze odpalić in-flight tick — nie dodawaj jego klatki.
                if (_recording) _frames.Add(path);
                else try { File.Delete(path); } catch { }
            }
        }
        catch { }
    }

    // ── DCIM / Gallery ────────────────────────────────────────────

    private static string GetDcimPath(string filename)
    {
#if ANDROID
        var dcim = Android.OS.Environment
            .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDcim)
            ?.AbsolutePath ?? FileSystem.AppDataDirectory;
        var dir = Path.Combine(dcim, "Camera");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, filename);
#else
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, filename);
#endif
    }

    private static void NotifyMediaStore(string path, string mimeType)
    {
#if ANDROID
        try
        {
            Android.Media.MediaScannerConnection.ScanFile(
                Android.App.Application.Context,
                [path], [mimeType], null);
        }
        catch { }
#endif
    }
}
