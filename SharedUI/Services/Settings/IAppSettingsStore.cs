namespace SharedUI.Services.Settings;

public interface IAppSettingsStore
{
    ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
