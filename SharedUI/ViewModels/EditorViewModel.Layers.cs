using SharedUI.Services;

namespace SharedUI.ViewModels;

public sealed partial class EditorViewModel
{
    private sealed record LayerEntry(Guid Id, string Name, byte[] Bytes, bool Visible);
    private readonly List<LayerEntry> _layers = new();
    private int _activeLayerIndex = -1;

    public bool HasActiveLayer => HasImage && _activeLayerIndex >= 0 && _activeLayerIndex < _layers.Count;
    public bool CanDeleteLayer => HasImage && _layers.Count > 1 && _activeLayerIndex >= 0;
    public bool CanMergeDown => HasImage && _layers.Count > 1 && _activeLayerIndex > 0;
    public bool CanToggleLayerVisibility => HasImage && _layers.Count > 0;

    public int ActiveLayerIndex => _activeLayerIndex;

    public IReadOnlyList<(Guid Id, string Name, bool Visible, int Index)> Layers
    {
        get
        {
            if (_layers.Count == 0)
                return Array.Empty<(Guid, string, bool, int)>();

            var result = new (Guid, string, bool, int)[_layers.Count];
            for (var i = 0; i < _layers.Count; i++)
                result[i] = (_layers[i].Id, _layers[i].Name, _layers[i].Visible, i);

            return result;
        }
    }

    private byte[]? ActiveLayerBytes
        => _activeLayerIndex >= 0 && _activeLayerIndex < _layers.Count ? _layers[_activeLayerIndex].Bytes : null;

    private void LayersReset()
    {
        _layers.Clear();
        _activeLayerIndex = -1;
    }

    private void LayersInitFromDocumentBytes(byte[] bytes)
    {
        _layers.Clear();
        _layers.Add(new LayerEntry(Guid.NewGuid(), "Base", bytes, Visible: true));
        _activeLayerIndex = 0;
        UpdateViewFromLayers();
    }

    private void SetActiveLayerBytes(byte[] bytes)
    {
        if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count)
            return;

        _layers[_activeLayerIndex] = _layers[_activeLayerIndex] with { Bytes = bytes };
    }

    private byte[]? GetCompositedBytesOrFallback()
    {
        if (_layers.Count == 0)
            return CurrentBytes;

        if (_layers.Count == 1)
            return _layers[0].Visible ? _layers[0].Bytes : CurrentBytes;

        var visible = _layers.Where(l => l.Visible).Select(l => l.Bytes).ToArray();
        if (visible.Length == 0)
            return CurrentBytes;

        return _imageProcessor.CompositeRgbaLayers(visible);
    }

    private void UpdateViewFromLayers()
    {
        _viewBytes = GetCompositedBytesOrFallback() ?? _viewBytes;
    }

    public async Task AddLayerAsync()
    {
        if (!HasImage)
            return;

        var sizeSource = GetCompositedBytesOrFallback() ?? CurrentBytes;
        if (sizeSource is null)
            return;

        var (w, h) = await RunImageCpuAsync(() => _imageProcessor.GetSize(sizeSource), inProgressStatus: "Preparing...");
        if (w <= 0 || h <= 0)
            return;

        var created = _imageProcessor.CreateTransparent(w, h);
        _layers.Add(new LayerEntry(Guid.NewGuid(), $"Layer {_layers.Count + 1}", created.Bytes, Visible: true));
        _activeLayerIndex = _layers.Count - 1;

        UpdateViewFromLayers();
        NotifyAll();
    }

    public Task DuplicateLayerAsync()
    {
        if (!HasImage || _activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count)
            return Task.CompletedTask;

        var src = _layers[_activeLayerIndex];
        _layers.Add(new LayerEntry(Guid.NewGuid(), $"{src.Name} Copy", src.Bytes, src.Visible));
        _activeLayerIndex = _layers.Count - 1;

        UpdateViewFromLayers();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task DeleteLayerAsync()
    {
        if (!HasImage || !CanDeleteLayer)
            return Task.CompletedTask;

        _layers.RemoveAt(_activeLayerIndex);
        _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1);

        UpdateViewFromLayers();
        NotifyAll();
        return Task.CompletedTask;
    }

    public void SelectLayer(int index)
    {
        if (index < 0 || index >= _layers.Count)
            return;

        _activeLayerIndex = index;
        UpdateViewFromLayers();
        NotifyAll();
    }

    public Task MergeDownActiveLayerAsync()
    {
        if (!HasImage || !CanMergeDown)
            return Task.CompletedTask;

        var lowerIndex = _activeLayerIndex - 1;
        var lower = _layers[lowerIndex];
        var upper = _layers[_activeLayerIndex];

        var merged = _imageProcessor.CompositeRgbaLayers(new[] { lower.Bytes, upper.Bytes });
        _layers[lowerIndex] = lower with { Bytes = merged };
        _layers.RemoveAt(_activeLayerIndex);
        _activeLayerIndex = lowerIndex;

        UpdateViewFromLayers();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task ToggleLayerVisibilityAsync(int index)
    {
        if (!HasImage)
            return Task.CompletedTask;

        if (index < 0 || index >= _layers.Count)
            return Task.CompletedTask;

        var layer = _layers[index];
        _layers[index] = layer with { Visible = !layer.Visible };

        // If we just hid the active layer, move to a visible layer (if any).
        _ = EnsureActiveLayerEditable();

        UpdateViewFromLayers();
        NotifyAll();
        return Task.CompletedTask;
    }

    public bool IsActiveLayerVisible
        => _activeLayerIndex >= 0 && _activeLayerIndex < _layers.Count && _layers[_activeLayerIndex].Visible;

    private bool EnsureActiveLayerEditable()
    {
        if (!HasImage)
            return false;

        if (_layers.Count == 0)
            return false;

        if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count)
            _activeLayerIndex = 0;

        if (_layers[_activeLayerIndex].Visible)
            return true;

        // Pick nearest visible layer (prefer below, then above).
        for (var i = _activeLayerIndex - 1; i >= 0; i--)
        {
            if (_layers[i].Visible)
            {
                _activeLayerIndex = i;
                return true;
            }
        }

        for (var i = _activeLayerIndex + 1; i < _layers.Count; i++)
        {
            if (_layers[i].Visible)
            {
                _activeLayerIndex = i;
                return true;
            }
        }

        // No visible layers.
        return false;
    }

    private byte[] GetActiveLayerOrCurrentBytesOrThrow()
    {
        if (!EnsureActiveLayerEditable())
            throw new InvalidOperationException("No visible layer to edit.");

        return ActiveLayerBytes ?? CurrentBytes ?? throw new InvalidOperationException("No image bytes loaded.");
    }

    private void ApplyToActiveLayerAndRefresh(byte[] next)
    {
        if (!EnsureActiveLayerEditable())
            return;

        SetActiveLayerBytes(next);
        UpdateViewFromLayers();
    }
}
