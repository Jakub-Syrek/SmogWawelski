using System.Text.Json;
using SmogWawelski.Core;
using SmogWawelski.Services;
using SmogWawelski.ViewModels;
using GtfsShapeService = SmogWawelski.Core.GtfsShapeService;

namespace SmogWawelski;

public partial class MainPage : ContentPage
{
    private readonly MapViewModel       _vm         = new();
    private readonly GtfsShapeService   _shapeSvc   = new(FileSystem.AppDataDirectory);
    private readonly ScreenCaptureService _capture  = new();
    private bool _mapReady;
    private bool _routesDrawn;
    private int  _recordSeconds;
    private IDispatcherTimer? _recordClock;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = _vm;
        LinesList.ItemsSource = _vm.TramLines;

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
        _vm.StartAutoRefresh(5);
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
            var routes = await _shapeSvc.GetRoutesAsync();
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
            { v.Id, v.Name, v.Lat, v.Lng, v.Color, Heading = v.Heading }));
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
        _capture.StartRecording(Dispatcher);

        // Pulsujący czerwony przycisk
        RecordBtn.Text             = "⏹";
        RecordBorder.BackgroundColor = Color.FromRgb(100, 0, 0);
        RecordingIndicator.IsVisible = true;

        // Zegar sekundowy
        _recordClock          = Dispatcher.CreateTimer();
        _recordClock.Interval = TimeSpan.FromSeconds(1);
        _recordClock.Tick    += (_, _) =>
        {
            _recordSeconds++;
            RecordingLabel.Text = $"REC {_recordSeconds}s";
            // Auto-stop po 30 sekundach
            if (_recordSeconds >= 30)
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

        StatusLabel.Text = "Przygotowuję klatki…";
        var frames = await _capture.StopAndGetFramesAsync();

        if (frames.Count >= 2)
            await ScreenCaptureService.ShareFramesAsync(frames, $"MPK Kraków — {frames.Count} klatek");
        else
            await DisplayAlert("Info", "Za mało klatek — nagraj dłużej niż 2 sekundy", "OK");

        StatusLabel.Text = _vm.Status;
    }
}
