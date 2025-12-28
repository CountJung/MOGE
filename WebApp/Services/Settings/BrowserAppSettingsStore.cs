using System.Text.Json;
using Microsoft.JSInterop;
using SharedUI.Services.Settings;

namespace WebApp.Services.Settings;

public sealed class BrowserAppSettingsStore : IAppSettingsStore
{
    private const string Empty = "";

    private readonly IJSRuntime _js;

    public BrowserAppSettingsStore(IJSRuntime js)
    {
        _js = js;
    }

    public async ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("mogeSettings.get", cancellationToken);
            if (string.IsNullOrWhiteSpace(json) || string.Equals(json, Empty, StringComparison.Ordinal))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings);
            await _js.InvokeVoidAsync("mogeSettings.set", cancellationToken, json);
        }
        catch
        {
            // ignore
        }
    }
}
