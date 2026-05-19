using System.Text.Json;
using SmogWawelski.Core;
using SmogWawelski.Services;
using SmogWawelski.ViewModels;

namespace SmogWawelski;

public partial class MainPage : ContentPage
{
    private readonly MapViewModel         _vm      = new();
    private readonly ApiClient            _api     = new();
    private readonly ScreenCaptureService _capture = new();
    private bool _mapReady;
    private bool _routesDrawn;
    private int  _recordSeconds;
    private IDispatcherTimer? _recordClock;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        LinesList.ItemsSource = _vm.TramLines;
        _capture.SetMapView(MapView);

        _vm.OnVehiclesUpdated = PushVehiclesToMapAsync;
        _vm.PropertyChanged += (_, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (e.PropertyName == nameof(_vm.Status))
                    StatusLabel.Text = _vm.Status;
                if (e.PropertyName == nameof(_vm.IsLoading))
                    Spinner.IsRunning = _vm.IsLoading;
                if (e.PropertyName == nameof(_vm.TramCount))
                {
                    CountLabel.Text = _vm.TramCount.ToString();
                    _ = CountBadge.ScaleToAsync(1.3, 120)
                                  .ContinueWith(_ => CountBadge.ScaleToAsync(1.0, 120));
                }
            });
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadMap();
        _vm.StartAutoRefresh(2);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopAutoRefresh();
    }

    // ── Mapa ─────────────────────────────────────────────────────

    private void LoadMap()
    {
        MapView.Source = new HtmlWebViewSource { Html = LoadMapHtml() };
        MapView.Navigated += async (_, _) =>
        {
            _mapReady = true;
            await LoadRoutesAsync();
        };
    }

    private static string LoadMapHtml()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("map.html").Result;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"<html><body style='background:#0d1117;color:white;padding:20px'>Błąd: {ex.Message}</body></html>";
        }
    }

    private async Task LoadRoutesAsync()
    {
        if (_routesDrawn) return;
        try
        {
            var routes = await _api.GetShapesAsync();
            if (routes.Count == 0) return;
            var json    = JsonSerializer.Serialize(routes);
            var escaped = json.Replace("\\","\\\\").Replace("'","\\'").Replace("\n","").Replace("\r","");
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try   { await MapView.EvaluateJavaScriptAsync($"drawRoutes('{escaped}');"); _routesDrawn = true; }
                catch { }
            });
        }
        catch { }
    }

    private async Task PushVehiclesToMapAsync(List<TtssVehicle> vehicles)
    {
        if (!_mapReady) return;
        var payload = JsonSerializer.Serialize(vehicles.Select(v => new
            { v.Id, v.Name, v.Kind, v.Lat, v.Lng, v.Color, Heading = v.Heading,
              VLat = v.VLat, VLng = v.VLng, Ts = v.Ts }));
        var escaped = payload.Replace("\\","\\\\").Replace("'","\\'").Replace("\n","").Replace("\r","");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await MapView.EvaluateJavaScriptAsync($"updateVehicles('{escaped}');"); }
            catch { }
        });
    }

    // ── Eventy — UI ──────────────────────────────────────────────

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        _ = RefreshBtn.RotateToAsync(360, 400)
                      .ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => RefreshBtn.Rotation = 0));
        _ = _vm.RefreshAsync();
    }

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(e.NewTextValue);
        ClearFilterBtn.IsVisible = hasFilter;
        if (!hasFilter) _vm.FilterLine = "";
    }

    private void OnFilterChanged(object? sender, EventArgs e)
        => _vm.FilterLine = FilterEntry.Text?.Trim() ?? "";

    private void OnClearFilter(object? sender, EventArgs e)
    {
        FilterEntry.Text    = "";
        ClearFilterBtn.IsVisible = false;
        _vm.FilterLine      = "";
    }

    private async void OnLineSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TramRow row) return;
        _vm.FilterLine            = row.Line;
        FilterEntry.Text          = row.Line;
        ClearFilterBtn.IsVisible  = true;
        ((CollectionView)sender!).SelectedItem = null;

        await Task.Delay(400);
        if (!_mapReady || _vm.LastVehicles.Count == 0) return;
        var pts     = JsonSerializer.Serialize(_vm.LastVehicles.Select(v => new { v.Lat, v.Lng }));
        var escaped = pts.Replace("\\","\\\\").Replace("'","\\'");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await MapView.EvaluateJavaScriptAsync($"focusVehicles('{escaped}');"); }
            catch { }
        });
    }

    // ── Screenshot ───────────────────────────────────────────────

    private async void OnCenterClicked(object? sender, EventArgs e)
    {
        if (!_mapReady) return;

        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status == PermissionStatus.Granted)
            {
                var loc = await Geolocation.Default.GetLastKnownLocationAsync()
                          ?? await Geolocation.Default.GetLocationAsync(new GeolocationRequest(
                                  GeolocationAccuracy.Medium, TimeSpan.FromSeconds(8)));

                if (loc != null && loc.Latitude is > 49 and < 51 && loc.Longitude is > 19 and < 21)
                {
                    var lat = loc.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var lng = loc.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try { await MapView.EvaluateJavaScriptAsync($"centerOnUser({lat},{lng});"); } catch { }
                    });
                    return;
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[Geo] {ex.Message}"); }

        // Fallback — środek Krakowa
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await MapView.EvaluateJavaScriptAsync("map.setView([50.0614,19.9366],14,{animate:true,duration:0.6});"); }
            catch { }
        });
    }

    private async void OnScreenshotClicked(object? sender, EventArgs e)
    {
        var path = await _capture.TakeScreenshotAsync();
        if (path == null) { await DisplayAlert("Błąd", "Nie udało się zrobić zrzutu", "OK"); return; }
        await ScreenCaptureService.ShareFileAsync(path, "MPK Kraków — zrzut ekranu");
    }

    // ── Recording ────────────────────────────────────────────────

    private async void OnRecordClicked(object? sender, EventArgs e)
    {
        if (_capture.IsRecording)
            await StopRecordingAsync();
        else
            StartRecording();
    }

    private void StartRecording()
    {
        _recordSeconds = 0;
        // 100ms = 10fps — wystarczająco płynnie dla MP4, a Screenshot.CaptureAsync wyrabia
        _capture.StartRecording(Dispatcher, fpsMs: 100, maxSeconds: 120);

        RecordBtn.Text               = "⏹";
        RecordBorder.BackgroundColor = Color.FromRgb(100, 0, 0);
        RecordingIndicator.IsVisible = true;

        _recordClock          = Dispatcher.CreateTimer();
        _recordClock.Interval = TimeSpan.FromSeconds(1);
        _recordClock.Tick    += (_, _) =>
        {
            _recordSeconds++;
            RecordingLabel.Text = $"REC {_recordSeconds}s · {_capture.FrameCount}f";
            if (_recordSeconds >= 120)
                MainThread.BeginInvokeOnMainThread(async () => await StopRecordingAsync());
        };
        _recordClock.Start();
    }

    private async Task StopRecordingAsync()
    {
        _recordClock?.Stop();
        RecordBtn.Text               = "⏺";
        RecordBorder.BackgroundColor = Color.FromRgb(33, 38, 45);
        RecordingIndicator.IsVisible = false;
        RecordingLabel.Text          = "REC 0s";

        if (_capture.FrameCount < 2)
        {
            await DisplayAlert("Info", "Za mało klatek — nagraj dłużej niż 2 sekundy", "OK");
            return;
        }

        var outPath = await _capture.StopAndSaveAsync(frameDelayMs: 100,
            status => MainThread.BeginInvokeOnMainThread(() => StatusLabel.Text = status));

        if (outPath != null)
        {
            var ext = Path.GetExtension(outPath).TrimStart('.').ToUpperInvariant();
            StatusLabel.Text = $"✅ {ext} zapisany do DCIM/Camera";

            var action = await DisplayActionSheet(
                "Co zrobić z nagraniem?",
                "Pomiń", null,
                "📘 Facebook", "📤 Udostępnij...", "📁 Pokaż w folderze");

            switch (action)
            {
                case "📘 Facebook":
                    await ShareToFacebookAsync(outPath);
                    break;
                case "📤 Udostępnij...":
                    await ScreenCaptureService.ShareFileAsync(outPath, "MPK Kraków — tramwaje live");
                    break;
                case "📁 Pokaż w folderze":
                    OpenContainingFolder(outPath);
                    break;
            }
        }
        else
        {
            await DisplayAlert("Błąd", "Nie udało się zapisać nagrania", "OK");
        }

        StatusLabel.Text = _vm.Status;
    }

    // ── Facebook share ──────────────────────────────────────────
    private async Task ShareToFacebookAsync(string filePath)
    {
#if ANDROID
        // Spróbuj uruchomić appkę Facebooka z plikiem (com.facebook.katana).
        // Jeśli nie zainstalowana — fallback do generic share, gdzie user wybierze FB Lite/Messenger itd.
        try
        {
            var ctx = Android.App.Application.Context;
            var file = new Java.IO.File(filePath);
            var authority = ctx.PackageName + ".fileProvider";
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, authority, file);
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionSend);
            intent.SetType("video/mp4");
            intent.PutExtra(Android.Content.Intent.ExtraStream, uri);
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);
            intent.SetPackage("com.facebook.katana");
            ctx.StartActivity(intent);
            return;
        }
        catch (Android.Content.ActivityNotFoundException)
        {
            // FB nie zainstalowany — fallback do generic share
            await ScreenCaptureService.ShareFileAsync(filePath, "MPK Kraków — tramwaje live");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FB] Intent failed: {ex.Message}");
        }
#endif
        // Windows / iOS / fallback: otwórz facebook.com w przeglądarce + pokaż folder żeby user mógł upload-nąć
        try
        {
            await Launcher.OpenAsync("https://www.facebook.com/");
            OpenContainingFolder(filePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FB] Launch failed: {ex.Message}");
            await ScreenCaptureService.ShareFileAsync(filePath, "MPK Kraków — tramwaje live");
        }
    }

    private static void OpenContainingFolder(string filePath)
    {
        try
        {
#if WINDOWS
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
#else
            // Inne platformy — otwórz katalog jako URI
            _ = Launcher.OpenAsync(new OpenFileRequest("Folder", new ReadOnlyFile(filePath)));
#endif
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Folder] Open failed: {ex.Message}");
        }
    }
}
