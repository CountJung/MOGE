namespace SharedUI.Services.Settings;

public sealed class AppSettingsService
{
    private readonly IAppSettingsStore _store;

    private bool _initialized;
    private AppSettings _current = new();

    public AppSettingsService(IAppSettingsStore store)
    {
        _store = store;
    }

    public AppSettings Current => _current;

    public event Action<AppSettings>? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        _current = await _store.LoadAsync(cancellationToken);
        _initialized = true;
        Changed?.Invoke(_current);
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
            await InitializeAsync(cancellationToken);

        var next = update(_current);
        if (Equals(next, _current))
            return;

        _current = next;
        await _store.SaveAsync(_current, cancellationToken);
        Changed?.Invoke(_current);
    }
}
