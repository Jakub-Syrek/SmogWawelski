#if WINDOWS
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;
#endif

namespace SmogWawelski.Services;

/// <summary>
/// Cross-platform encoder MP4 (H.264). Windows: MediaComposition.
/// Android: TODO (MediaCodec) — na razie wyjątek, fallback do GIF w wywołującym.
/// </summary>
public static class Mp4Writer
{
    /// <summary>Zakoduj sekwencję klatek PNG do pliku MP4.</summary>
    /// <param name="framePaths">Ścieżki do PNG w kolejności chronologicznej.</param>
    /// <param name="outputPath">Pełna ścieżka docelowa pliku .mp4.</param>
    /// <param name="frameDelayMs">Czas trwania jednej klatki (ms). 100 = 10 fps.</param>
    public static async Task CreateAsync(IList<string> framePaths, string outputPath, int frameDelayMs = 100)
    {
        if (framePaths.Count < 2) throw new ArgumentException("Need at least 2 frames");

#if WINDOWS
        await EncodeWindowsAsync(framePaths, outputPath, frameDelayMs);
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("MP4 encoding not yet implemented on this platform");
#endif
    }

    public static bool IsSupported
    {
        get
        {
#if WINDOWS
            return true;
#else
            return false;
#endif
        }
    }

#if WINDOWS
    private static async Task EncodeWindowsAsync(IList<string> framePaths, string outputPath, int frameDelayMs)
    {
        var composition = new MediaComposition();
        var dur = TimeSpan.FromMilliseconds(frameDelayMs);

        foreach (var path in framePaths)
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var clip = await MediaClip.CreateFromImageFileAsync(file, dur);
            composition.Clips.Add(clip);
        }

        var dir = Path.GetDirectoryName(outputPath)!;
        var name = Path.GetFileName(outputPath);
        var folder = await StorageFolder.GetFolderFromPathAsync(dir);
        var outFile = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        await composition.RenderToFileAsync(outFile, MediaTrimmingPreference.Precise, profile);
    }
#endif
}
