using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SmogWawelski.Models;
using SmogWawelski.Services;

namespace SmogWawelski.ViewModels;

public class TramRow
{
    public string Line      { get; set; } = "";
    public string Color     { get; set; } = "#E20D19";
    public string ColorDark { get; set; } = "#8B0000";
    public int    Count     { get; set; }
    public string Badge     => Count > 0 ? $"{Count} 🚋" : "";
}

public class MapViewModel : INotifyPropertyChanged
{
    private readonly TtssService _svc = new();
    private CancellationTokenSource _cts = new();

    private string _status = "Ładowanie...";
    private bool _isLoading;
    private string _filterLine = "";

    public ObservableCollection<TramRow> TramLines { get; } = [];

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public string FilterLine
    {
        get => _filterLine;
        set { _filterLine = value; OnPropertyChanged(); _ = RefreshAsync(); }
    }

    // Ostatnia pełna lista pojazdów do renderowania mapy
    public List<TtssVehicle> LastVehicles { get; private set; } = [];

    // Callback — MainPage podpina aktualizację mapy
    public Func<List<TtssVehicle>, Task>? OnVehiclesUpdated { get; set; }

    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var all = await _svc.GetTramsAsync(_cts.Token);

            LastVehicles = string.IsNullOrWhiteSpace(FilterLine)
                ? all
                : all.Where(v => v.Name == FilterLine).ToList();

            // Aktualizuj listę linii
            var grouped = all
                .GroupBy(v => v.Name)
                .OrderBy(g => g.Key.PadLeft(3, '0'))
                .Select(g => new TramRow
                {
                    Line      = g.Key,
                    Color     = g.First().Color,
                    ColorDark = DarkenHex(g.First().Color),
                    Count     = g.Count()
                })
                .ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                TramLines.Clear();
                foreach (var row in grouped) TramLines.Add(row);
            });

            OnPropertyChanged(nameof(TramCount));

            if (LastVehicles.Count == 0 && !string.IsNullOrEmpty(_svc.LastError))
                Status = $"Błąd: {_svc.LastError[..Math.Min(60, _svc.LastError.Length)]}";
            else
                Status = $"Live · {DateTime.Now:HH:mm:ss} · {LastVehicles.Count} tramwajów";

            if (OnVehiclesUpdated != null)
                await OnVehiclesUpdated(LastVehicles);
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Przyciemnia kolor hex o ~30%
    private static string DarkenHex(string hex)
    {
        try
        {
            var c = hex.TrimStart('#');
            int r = Convert.ToInt32(c[..2], 16);
            int g = Convert.ToInt32(c[2..4], 16);
            int b = Convert.ToInt32(c[4..6], 16);
            return $"#{Math.Max(0,r-60):X2}{Math.Max(0,g-60):X2}{Math.Max(0,b-60):X2}";
        }
        catch { return "#111111"; }
    }

    public int TramCount => LastVehicles.Count;

    public void StartAutoRefresh(int intervalSeconds = 5)
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                await RefreshAsync();
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ContinueWith(_ => { });
            }
        }, ct);
    }

    public void StopAutoRefresh() => _cts.Cancel();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? p = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
