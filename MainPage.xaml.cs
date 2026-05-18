using System.Text.Json;
using SmogWawelski.Models;
using SmogWawelski.Services;
using SmogWawelski.ViewModels;

namespace SmogWawelski;

public partial class MainPage : ContentPage
{
    private readonly MapViewModel     _vm       = new();
    private readonly GtfsShapeService _shapeSvc = new(FileSystem.AppDataDirectory);
    private bool _mapReady;
    private bool _routesDrawn;

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
                {
                    StatusLabel.Text = _vm.Status;
                }
                if (e.PropertyName == nameof(_vm.IsLoading))
                {
                    Spinner.IsRunning = _vm.IsLoading;
                    RefreshBtn.Rotation = _vm.IsLoading ? 0 : 0;
                }
                if (e.PropertyName == nameof(_vm.TramCount))
                {
                    CountLabel.Text = _vm.TramCount.ToString();
                    // Animacja badge gdy liczba się zmienia
                    _ = CountBadge.ScaleToAsync(1.3, 120).ContinueWith(_ =>
                        CountBadge.ScaleToAsync(1.0, 120));
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

    // ── Mapa ────────────────────────────────────────────────────────────────

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
            return $"<html><body style='background:#0d1117;color:white;padding:20px'>Błąd mapy: {ex.Message}</body></html>";
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
                try
                {
                    await MapView.EvaluateJavaScriptAsync($"drawRoutes('{escaped}');");
                    _routesDrawn = true;
                    Console.WriteLine($"[Map] Narysowano {routes.Count} tras");
                }
                catch (Exception ex) { Console.WriteLine($"[Map] drawRoutes błąd: {ex.Message}"); }
            });
        }
        catch (Exception ex) { Console.WriteLine($"[Map] LoadRoutes błąd: {ex.Message}"); }
    }

    private async Task PushVehiclesToMapAsync(List<TtssVehicle> vehicles)
    {
        if (!_mapReady) return;

        var payload = JsonSerializer.Serialize(vehicles.Select(v => new
        {
            v.Id, v.Name, v.Lat, v.Lng, v.Color, Heading = v.Heading
        }));

        // Bezpieczne przekazanie do JS przez JSON.parse zamiast inline string
        var jsonEscaped = payload
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\n", "")
            .Replace("\r", "");

        var js = $"updateVehicles('{jsonEscaped}');";

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await MapView.EvaluateJavaScriptAsync(js); }
            catch { /* mapa jeszcze się ładuje */ }
        });
    }

    // ── Eventy ──────────────────────────────────────────────────────────────

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        _ = RefreshBtn.RotateToAsync(360, 400).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() => RefreshBtn.Rotation = 0));
        _ = _vm.RefreshAsync();
    }

    private void OnFilterChanged(object? sender, EventArgs e)
        => _vm.FilterLine = FilterEntry.Text?.Trim() ?? "";

    private void OnClearFilter(object? sender, EventArgs e)
    {
        FilterEntry.Text = "";
        _vm.FilterLine = "";
    }

    private async void OnLineSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TramRow row) return;
        _vm.FilterLine = row.Line;
        FilterEntry.Text = row.Line;
        ((CollectionView)sender!).SelectedItem = null;

        await Task.Delay(400);
        if (!_mapReady || _vm.LastVehicles.Count == 0) return;

        var pts = JsonSerializer.Serialize(
            _vm.LastVehicles.Select(v => new { v.Lat, v.Lng }));
        var escaped = pts.Replace("\\","\\\\").Replace("'","\\'");
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try { await MapView.EvaluateJavaScriptAsync($"focusVehicles('{escaped}');"); }
            catch { }
        });
    }
}
