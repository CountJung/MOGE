using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using SharedUI.Logging;
using SharedUI.Mvvm;
using SharedUI.Services.Settings;

namespace SharedUI.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettingsService _settingsService;
    private readonly NavigationManager _nav;
    private readonly IJSRuntime _js;
    private readonly ILogExportService _logExport;
    private readonly MogeLogService _log;

    private AppThemeMode _theme;

    private double _touchMinScale;
    private double _touchMaxScale;
    private double _touchPinchExponent;

    private bool _touchInertiaEnabled;
    private double _touchInertiaStartSpeed;
    private double _touchInertiaStopSpeed;
    private double _touchInertiaDecayPer16ms;

    private string? _logExportStatus;

    public SettingsViewModel(AppSettingsService settingsService, NavigationManager nav, IJSRuntime js, ILogExportService logExport, MogeLogService log)
    {
        _settingsService = settingsService;
        _nav = nav;
        _js = js;
        _logExport = logExport;
        _log = log;
    }

    public AppThemeMode Theme => _theme;

    public double TouchMinScale => _touchMinScale;
    public double TouchMaxScale => _touchMaxScale;
    public double TouchPinchExponent => _touchPinchExponent;

    public bool TouchInertiaEnabled => _touchInertiaEnabled;
    public double TouchInertiaStartSpeed => _touchInertiaStartSpeed;
    public double TouchInertiaStopSpeed => _touchInertiaStopSpeed;
    public double TouchInertiaDecayPer16ms => _touchInertiaDecayPer16ms;

    public string? LogExportStatus => _logExportStatus;

    public async Task InitializeAsync()
    {
        await _settingsService.InitializeAsync();
        ApplyFrom(_settingsService.Current);
        _settingsService.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(AppSettings s) => ApplyFrom(s);

    private void ApplyFrom(AppSettings s)
    {
        SetProperty(ref _theme, s.ThemeMode, nameof(Theme));

        SetProperty(ref _touchMinScale, s.TouchMinScale, nameof(TouchMinScale));
        SetProperty(ref _touchMaxScale, s.TouchMaxScale, nameof(TouchMaxScale));
        SetProperty(ref _touchPinchExponent, s.TouchPinchExponent, nameof(TouchPinchExponent));

        SetProperty(ref _touchInertiaEnabled, s.TouchInertiaEnabled, nameof(TouchInertiaEnabled));
        SetProperty(ref _touchInertiaStartSpeed, s.TouchInertiaStartSpeed, nameof(TouchInertiaStartSpeed));
        SetProperty(ref _touchInertiaStopSpeed, s.TouchInertiaStopSpeed, nameof(TouchInertiaStopSpeed));
        SetProperty(ref _touchInertiaDecayPer16ms, s.TouchInertiaDecayPer16ms, nameof(TouchInertiaDecayPer16ms));
    }

    public Task OnThemeChanged(AppThemeMode mode)
        => _settingsService.UpdateAsync(s => s with { ThemeMode = mode });

    public Task OnTouchMinScaleChanged(double v)
        => _settingsService.UpdateAsync(s =>
        {
            var min = Math.Clamp(v, 0.01, 10.0);
            var max = Math.Max(min, s.TouchMaxScale);
            return s with { TouchMinScale = min, TouchMaxScale = max };
        });

    public Task OnTouchMaxScaleChanged(double v)
        => _settingsService.UpdateAsync(s =>
        {
            var max = Math.Clamp(v, 0.1, 50.0);
            var min = Math.Min(s.TouchMinScale, max);
            return s with { TouchMinScale = min, TouchMaxScale = max };
        });

    public Task OnTouchPinchExponentChanged(double v)
        => _settingsService.UpdateAsync(s => s with { TouchPinchExponent = Math.Clamp(v, 0.5, 2.0) });

    public Task OnTouchInertiaEnabledChanged(bool enabled)
        => _settingsService.UpdateAsync(s => s with { TouchInertiaEnabled = enabled });

    public Task OnTouchInertiaStartSpeedChanged(double v)
        => _settingsService.UpdateAsync(s =>
        {
            var start = Math.Clamp(v, 0.0, 1.0);
            var stop = Math.Min(s.TouchInertiaStopSpeed, start);
            return s with { TouchInertiaStartSpeed = start, TouchInertiaStopSpeed = stop };
        });

    public Task OnTouchInertiaStopSpeedChanged(double v)
        => _settingsService.UpdateAsync(s =>
        {
            var stop = Math.Clamp(v, 0.0, 1.0);
            var start = Math.Max(s.TouchInertiaStartSpeed, stop);
            return s with { TouchInertiaStartSpeed = start, TouchInertiaStopSpeed = stop };
        });

    public Task OnTouchInertiaDecayChanged(double v)
        => _settingsService.UpdateAsync(s => s with { TouchInertiaDecayPer16ms = Math.Clamp(v, 0.80, 0.99) });

    public async Task GoBackAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("history.back");
        }
        catch
        {
            // Fallback when history isn't available (e.g., direct landing).
            _nav.NavigateTo("");
        }
    }

    public async Task ExportLatestLogsAsync()
    {
        try
        {
            var result = await _logExport.ExportLatestAsync($"moge-log-{DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd}.txt");
            _logExportStatus = result.Message;
            OnPropertyChanged(nameof(LogExportStatus));
        }
        catch (Exception ex)
        {
            _log.Log(LogLevel.Error, "Settings", "Log export failed", ex);
            _logExportStatus = "로그 내보내기에 실패했습니다. 다시 시도해 주세요.";
            OnPropertyChanged(nameof(LogExportStatus));
        }
    }

    public void Dispose()
    {
        _settingsService.Changed -= OnSettingsChanged;
    }
}
