using System.Text.Json;
using Microsoft.Maui.Storage;
using SharedUI.Services.Settings;

namespace HybridApp.Services.Settings;

public sealed class MauiAppSettingsStore : IAppSettingsStore
{
    private const string Key = "moge.settings.v1";

    public ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = Preferences.Default.Get(Key, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return ValueTask.FromResult(new AppSettings());

            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return ValueTask.FromResult(settings ?? new AppSettings());
        }
        catch
        {
            return ValueTask.FromResult(new AppSettings());
        }
    }

    public ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings);
            Preferences.Default.Set(Key, json);
        }
        catch
        {
            // ignore
        }

        return ValueTask.CompletedTask;
    }
}
