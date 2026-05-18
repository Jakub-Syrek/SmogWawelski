namespace SmogWawelski.Services;

/// <summary>
/// Screenshot (PNG) + recording (zestaw klatek PNG) — bez zewnętrznych zależności.
/// Udostępnia pliki przez MAUI Share API.
/// </summary>
public class ScreenCaptureService
{
    private bool _recording;
    private IDispatcherTimer? _timer;
    private readonly List<string> _frames = [];
    private readonly string _cacheDir;

    public bool IsRecording => _recording;

    public ScreenCaptureService()
    {
        _cacheDir = Path.Combine(FileSystem.CacheDirectory, "capture");
        Directory.CreateDirectory(_cacheDir);
    }

    // ── Screenshot ────────────────────────────────────────────────

    public async Task<string?> TakeScreenshotAsync()
    {
        try
        {
            var shot = await Screenshot.CaptureAsync();
            var path = Path.Combine(_cacheDir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            using var stream = await shot.OpenReadAsync();
            using var fs     = File.Create(path);
            await stream.CopyToAsync(fs);
            return path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Capture] Screenshot error: {ex.Message}");
            return null;
        }
    }

    public static async Task ShareFileAsync(string path, string title)
    {
        await Share.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File  = new ShareFile(path)
        });
    }

    // ── Recording ─────────────────────────────────────────────────

    public void StartRecording(IDispatcher dispatcher, int fpsMs = 400)
    {
        if (_recording) return;
        _recording = true;
        _frames.Clear();

        _timer          = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(fpsMs);
        _timer.Tick    += async (_, _) => await CaptureFrameAsync();
        _timer.Start();
        Console.WriteLine("[Capture] Recording started");
    }

    /// <summary>
    /// Zatrzymuje nagrywanie i zwraca listę ścieżek PNG.
    /// Klatki są gotowe do udostępnienia lub konwersji na GIF.
    /// </summary>
    public async Task<List<string>> StopAndGetFramesAsync()
    {
        _recording = false;
        _timer?.Stop();
        _timer = null;
        Console.WriteLine($"[Capture] Recording stopped — {_frames.Count} frames");

        // Daj czas na zapisanie ostatniej klatki
        await Task.Delay(200);
        return [.._frames];
    }

    public static async Task ShareFramesAsync(List<string> framePaths, string title)
    {
        if (framePaths.Count == 0) return;

        if (framePaths.Count == 1)
        {
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = title,
                File  = new ShareFile(framePaths[0])
            });
            return;
        }

        // Wiele plików — udostępnij wszystkie naraz
        await Share.RequestAsync(new ShareMultipleFilesRequest
        {
            Title = title,
            Files = framePaths.Select(p => new ShareFile(p)).ToList()
        });
    }

    private async Task CaptureFrameAsync()
    {
        try
        {
            var shot = await Screenshot.CaptureAsync();
            var path = Path.Combine(_cacheDir, $"frame_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            using var stream = await shot.OpenReadAsync();
            using var fs     = File.Create(path);
            await stream.CopyToAsync(fs);
            _frames.Add(path);
        }
        catch { }
    }

    /// <summary>Czyści stare pliki z katalogu cache.</summary>
    public void CleanCache()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_cacheDir, "*.png"))
            {
                if ((DateTime.Now - File.GetCreationTime(f)).TotalDays > 1)
                    File.Delete(f);
            }
        }
        catch { }
    }
}
