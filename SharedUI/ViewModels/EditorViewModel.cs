using Microsoft.AspNetCore.Components;
using OpenCvSharp;
using SharedUI.Components;
using SharedUI.Mvvm;
using SharedUI.Services;

namespace SharedUI.ViewModels;

public sealed class EditorViewModel : ObservableObject, IDisposable
{
    private readonly IImageFilePicker _imageFilePicker;
    private readonly ImageDocumentState _document;
    private readonly ImageProcessorService _imageProcessor;
    private readonly IImageExportService _imageExport;

    private Action<string?> _pushFooterMessage;

    private bool _layoutShortcutsSubscribed;

    private byte[]? _viewBytes;
    private ElementReference _canvas;
    private bool _hasCanvas;

    private const int MaxHistoryEntries = 50;
    private sealed record HistoryEntry(byte[] Bytes, string Label, DateTime Timestamp, string? ThumbnailDataUrl);
    private readonly List<HistoryEntry> _history = new();
    private int _historyIndex = -1;

    private bool _perspectiveMode;
    private bool _cropMode;
    private bool _selectionMode;
    private List<CanvasPoint> _handles = new();
    private int _imageWidth;
    private int _imageHeight;

    private CanvasInteractionMode _interactionMode = CanvasInteractionMode.PanZoom;
    private int _brushRadius = 8;

    private int _selectionBlurKernelSize;
    private double _selectionSharpenAmount = 1.0;

    private int _blurKernelSize;
    private bool _grayscale;
    private bool _sepia;
    private bool _canny;
    private double _cannyT1 = 50;
    private double _cannyT2 = 150;
    private double _contrast = 1.0;
    private double _brightness;

    private CancellationTokenSource? _debounceCts;
    private string? _status;

    private sealed record LoadedImage(ImagePickResult Pick, string? ThumbnailDataUrl);
    private readonly List<LoadedImage> _loadedImages = new();
    private int _selectedLoadedIndex = -1;

    public EditorViewModel(
        IImageFilePicker imageFilePicker,
        ImageDocumentState document,
        ImageProcessorService imageProcessor,
        IImageExportService imageExport,
        Action<string?>? pushFooterMessage = null)
    {
        _imageFilePicker = imageFilePicker;
        _document = document;
        _imageProcessor = imageProcessor;
        _imageExport = imageExport;
        _pushFooterMessage = pushFooterMessage ?? (_ => { });
    }

    public bool HasImage => _document.HasImage;
    public string? FileName => _document.FileName;
    public long FileSizeBytes => _document.Bytes?.Length ?? 0;
    public string? ContentType => _document.ContentType;

    public byte[]? ViewBytes => _viewBytes;

    public bool PerspectiveMode => _perspectiveMode;
    public bool CropMode => _cropMode;
    public bool SelectionMode => _selectionMode;

    public CanvasInteractionMode InteractionMode => _interactionMode;
    public int BrushRadius => _brushRadius;

    public IReadOnlyList<CanvasPoint> Handles => _handles;

    public int SelectionBlurKernelSize => _selectionBlurKernelSize;
    public double SelectionSharpenAmount => _selectionSharpenAmount;

    public int BlurKernelSize => _blurKernelSize;
    public bool Grayscale => _grayscale;
    public bool Sepia => _sepia;
    public bool Canny => _canny;
    public double CannyT1 => _cannyT1;
    public double CannyT2 => _cannyT2;
    public double Contrast => _contrast;
    public double Brightness => _brightness;

    public int SelectedLoadedIndex => _selectedLoadedIndex;

    public bool CanUndo => HasImage && _historyIndex > 0;
    public bool CanRedo => HasImage && _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public int CurrentHistoryIndex => _historyIndex;

    public IReadOnlyList<EditorHistoryListItem> HistoryItems => GetHistoryItems();

    public IReadOnlyList<(string FileName, string? ThumbnailDataUrl, int Index)> LoadedImages
    {
        get
        {
            if (_loadedImages.Count == 0)
                return Array.Empty<(string, string?, int)>();

            var result = new (string, string?, int)[_loadedImages.Count];
            for (var i = 0; i < _loadedImages.Count; i++)
                result[i] = (_loadedImages[i].Pick.FileName, _loadedImages[i].ThumbnailDataUrl, i);

            return result;
        }
    }

    private byte[]? CurrentBytes
        => _historyIndex >= 0 && _historyIndex < _history.Count
            ? _history[_historyIndex].Bytes
            : _document.Bytes;

    public void SetFooterPusher(Action<string?>? pushFooterMessage)
        => _pushFooterMessage = pushFooterMessage ?? (_ => { });

    public void Initialize()
    {
        _document.Changed += OnDocChanged;
        SyncFromDocument();
    }

    public void SetLayoutShortcutsSubscribed(bool subscribed) => _layoutShortcutsSubscribed = subscribed;
    public bool LayoutShortcutsSubscribed => _layoutShortcutsSubscribed;

    private void OnDocChanged()
    {
        SyncFromDocument();
        RefreshFooter();
        NotifyAll();
    }

    private void SyncFromDocument()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _perspectiveMode = false;
        _cropMode = false;
        _selectionMode = false;

        _history.Clear();
        _historyIndex = -1;

        _blurKernelSize = 0;
        _grayscale = false;
        _sepia = false;
        _canny = false;
        _cannyT1 = 50;
        _cannyT2 = 150;
        _contrast = 1.0;
        _brightness = 0;

        if (_document.Bytes is { Length: > 0 } bytes)
        {
            var thumb = _imageProcessor.CreateThumbnailDataUrl(bytes);
            _history.Add(new HistoryEntry(bytes, "Original", DateTime.UtcNow, thumb));
            _historyIndex = 0;
        }

        _viewBytes = CurrentBytes;
        _handles = new();
        (_imageWidth, _imageHeight) = _imageProcessor.GetSize(CurrentBytes);
        _status = null;
    }

    public async Task PickImagesAsync()
    {
        var picks = await _imageFilePicker.PickImagesAsync();
        if (picks.Count == 0)
            return;

        var firstNewIndex = _loadedImages.Count;
        foreach (var pick in picks)
        {
            string? thumb = null;
            try { thumb = _imageProcessor.CreateThumbnailDataUrl(pick.Bytes); } catch { }
            _loadedImages.Add(new LoadedImage(pick, thumb));
        }

        SelectLoadedImage(firstNewIndex);
        NotifyAll();
    }

    public Task ClearAsync()
    {
        _selectedLoadedIndex = -1;
        _document.Clear();
        NotifyAll();
        return Task.CompletedTask;
    }

    public void SelectLoadedImage(int index)
    {
        if (index < 0 || index >= _loadedImages.Count)
            return;

        if (_selectedLoadedIndex == index && HasImage)
            return;

        _selectedLoadedIndex = index;
        _document.Set(_loadedImages[index].Pick);
        NotifyAll();
    }

    public static string GetLoadedImageCardStyle(bool isSelected)
    {
        var border = isSelected
            ? "2px solid var(--mud-palette-primary)"
            : "1px solid var(--mud-palette-lines-default)";

        return $"border:{border}; border-radius: var(--mud-default-borderradius); cursor:pointer; user-select:none;";
    }

    public Task OnCanvasReady(ElementReference canvas)
    {
        _canvas = canvas;
        _hasCanvas = true;
        return Task.CompletedTask;
    }

    public async Task SavePngAsync()
    {
        if (!HasImage)
            return;

        if (!_hasCanvas)
            return;

        var baseName = FileNameUtil.GetSafeBaseName(FileName, "image");
        var filename = $"{baseName}-edited.png";

        await _imageExport.SavePngAsync(_canvas, filename);
        _status = $"Saved: {filename}";
        RefreshFooter();
        NotifyAll();
    }

    private void RefreshFooter() => _pushFooterMessage(_status);

    private IReadOnlyList<EditorHistoryListItem> GetHistoryItems()
    {
        if (_history.Count == 0)
            return Array.Empty<EditorHistoryListItem>();

        var items = new List<EditorHistoryListItem>(_history.Count);
        for (var i = 0; i < _history.Count; i++)
            items.Add(new EditorHistoryListItem(i, _history[i].Label, _history[i].ThumbnailDataUrl));

        return items;
    }

    public Task OnPerspectiveModeChanged(bool enabled)
    {
        _perspectiveMode = enabled;

        if (enabled)
            _interactionMode = CanvasInteractionMode.PanZoom;

        if (enabled)
            _cropMode = false;

        if (enabled)
            _selectionMode = false;

        if (!enabled)
        {
            _ = ApplyPipelineDebouncedAsync();
            RefreshFooter();
            NotifyAll();
            return Task.CompletedTask;
        }

        if (!HasImage)
        {
            _perspectiveMode = false;
            RefreshFooter();
            NotifyAll();
            return Task.CompletedTask;
        }

        var baseBytes = CurrentBytes;
        _viewBytes = baseBytes;

        (_imageWidth, _imageHeight) = _imageProcessor.GetSize(baseBytes);
        _handles = new List<CanvasPoint>
        {
            new(0, 0),
            new(_imageWidth, 0),
            new(_imageWidth, _imageHeight),
            new(0, _imageHeight)
        };

        _status = "Perspective: editing points";
        RefreshFooter();
        NotifyAll();

        return Task.CompletedTask;
    }

    public Task OnCropModeChanged(bool enabled)
    {
        _cropMode = enabled;

        if (enabled)
        {
            _perspectiveMode = false;
            _selectionMode = false;
            _interactionMode = CanvasInteractionMode.PanZoom;

            if (!HasImage)
            {
                _cropMode = false;
                RefreshFooter();
                NotifyAll();
                return Task.CompletedTask;
            }

            var baseBytes = CurrentBytes;
            _viewBytes = baseBytes;

            (_imageWidth, _imageHeight) = _imageProcessor.GetSize(baseBytes);

            var mx = Math.Max(1, (int)Math.Round(_imageWidth * 0.1));
            var my = Math.Max(1, (int)Math.Round(_imageHeight * 0.1));

            var x0 = mx;
            var y0 = my;
            var x1 = Math.Max(x0 + 1, _imageWidth - mx);
            var y1 = Math.Max(y0 + 1, _imageHeight - my);

            _handles = new List<CanvasPoint>
            {
                new(x0, y0),
                new(x1, y0),
                new(x1, y1),
                new(x0, y1)
            };

            _status = "Crop: adjust corners";
            RefreshFooter();
            NotifyAll();
            return Task.CompletedTask;
        }

        _handles = new();
        _status = null;
        RefreshFooter();
        NotifyAll();
        _ = ApplyPipelineDebouncedAsync();
        return Task.CompletedTask;
    }

    public Task OnSelectionModeChanged(bool enabled)
    {
        _selectionMode = enabled;

        if (enabled)
        {
            _perspectiveMode = false;
            _cropMode = false;
            _interactionMode = CanvasInteractionMode.PanZoom;

            if (!HasImage)
            {
                _selectionMode = false;
                RefreshFooter();
                NotifyAll();
                return Task.CompletedTask;
            }

            var baseBytes = CurrentBytes;
            _viewBytes = baseBytes;

            (_imageWidth, _imageHeight) = _imageProcessor.GetSize(baseBytes);

            var mx = Math.Max(1, (int)Math.Round(_imageWidth * 0.1));
            var my = Math.Max(1, (int)Math.Round(_imageHeight * 0.1));

            var x0 = mx;
            var y0 = my;
            var x1 = Math.Max(x0 + 1, _imageWidth - mx);
            var y1 = Math.Max(y0 + 1, _imageHeight - my);

            _handles = new List<CanvasPoint>
            {
                new(x0, y0),
                new(x1, y0),
                new(x1, y1),
                new(x0, y1)
            };

            _status = "Selection: adjust corners";
            RefreshFooter();
            NotifyAll();
            return Task.CompletedTask;
        }

        _handles = new();
        _status = null;
        RefreshFooter();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnInteractionModeChanged(CanvasInteractionMode mode)
    {
        _interactionMode = mode;

        if (mode != CanvasInteractionMode.PanZoom)
            _perspectiveMode = false;

        if (mode != CanvasInteractionMode.PanZoom)
            _cropMode = false;

        if (mode != CanvasInteractionMode.PanZoom)
            _selectionMode = false;

        _status = mode == CanvasInteractionMode.PanZoom ? null : $"Tool: {mode}";
        RefreshFooter();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnBrushRadiusChanged(int radius)
    {
        _brushRadius = Math.Clamp(radius, 1, 64);
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnHandlesChanged(IReadOnlyList<CanvasPoint> updated)
    {
        if (_cropMode || _selectionMode)
        {
            var xs = updated.Select(p => p.X).ToArray();
            var ys = updated.Select(p => p.Y).ToArray();

            var minX = Math.Clamp(xs.Min(), 0, _imageWidth);
            var maxX = Math.Clamp(xs.Max(), 0, _imageWidth);
            var minY = Math.Clamp(ys.Min(), 0, _imageHeight);
            var maxY = Math.Clamp(ys.Max(), 0, _imageHeight);

            if (maxX < minX) (minX, maxX) = (maxX, minX);
            if (maxY < minY) (minY, maxY) = (maxY, minY);

            _handles = new List<CanvasPoint>
            {
                new(minX, minY),
                new(maxX, minY),
                new(maxX, maxY),
                new(minX, maxY)
            };

            NotifyAll();
            return Task.CompletedTask;
        }

        _handles = updated.ToList();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnSelectionBlurKernelSizeChanged(int v)
    {
        _selectionBlurKernelSize = Math.Clamp(v, 0, 31);
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnSelectionSharpenAmountChanged(double v)
    {
        _selectionSharpenAmount = Math.Clamp(v, 0, 3);
        NotifyAll();
        return Task.CompletedTask;
    }

    public async Task ApplySelectionBlurAsync()
    {
        if (!HasImage || !_selectionMode)
            return;

        if (_handles.Count != 4)
        {
            _status = "Selection: expected 4 points";
            RefreshFooter();
            NotifyAll();
            return;
        }

        var (x0, y0, w, h) = GetHandlesRect();
        var kernel = _selectionBlurKernelSize;
        if (kernel <= 1)
        {
            _status = "Blur: kernel is 0";
            RefreshFooter();
            NotifyAll();
            return;
        }

        _status = "Blurring selection...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = CurrentBytes!;
        byte[] next;
        try
        {
            next = await Task.Run(() => _imageProcessor.BlurRegion(baseBytes, x0, y0, w, h, kernel));
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        CommitHistory(next, "Blur", preserveHandles: true);
        _status = "Blur applied";
        RefreshFooter();
        NotifyAll();
        await ApplyPipelineDebouncedAsync();
    }

    public async Task ApplySelectionSharpenAsync()
    {
        if (!HasImage || !_selectionMode)
            return;

        if (_handles.Count != 4)
        {
            _status = "Selection: expected 4 points";
            RefreshFooter();
            NotifyAll();
            return;
        }

        var (x0, y0, w, h) = GetHandlesRect();
        var amount = _selectionSharpenAmount;
        if (amount <= 0.0001)
        {
            _status = "Sharpen: amount is 0";
            RefreshFooter();
            NotifyAll();
            return;
        }

        _status = "Sharpening selection...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = CurrentBytes!;
        byte[] next;
        try
        {
            next = await Task.Run(() => _imageProcessor.SharpenRegion(baseBytes, x0, y0, w, h, amount));
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        CommitHistory(next, "Sharpen", preserveHandles: true);
        _status = "Sharpen applied";
        RefreshFooter();
        NotifyAll();
        await ApplyPipelineDebouncedAsync();
    }

    private (int x, int y, int w, int h) GetHandlesRect()
    {
        var minX = _handles.Min(p => p.X);
        var maxX = _handles.Max(p => p.X);
        var minY = _handles.Min(p => p.Y);
        var maxY = _handles.Max(p => p.Y);

        var x0 = (int)Math.Floor(Math.Clamp(minX, 0, _imageWidth - 1));
        var y0 = (int)Math.Floor(Math.Clamp(minY, 0, _imageHeight - 1));
        var x1 = (int)Math.Ceiling(Math.Clamp(maxX, x0 + 1, _imageWidth));
        var y1 = (int)Math.Ceiling(Math.Clamp(maxY, y0 + 1, _imageHeight));

        var w = Math.Max(1, x1 - x0);
        var h = Math.Max(1, y1 - y0);
        return (x0, y0, w, h);
    }

    public async Task ApplyCropAsync()
    {
        if (!HasImage)
            return;

        if (!_cropMode)
            return;

        if (_handles.Count != 4)
        {
            _status = "Crop: expected 4 points";
            RefreshFooter();
            NotifyAll();
            return;
        }

        var minX = _handles.Min(p => p.X);
        var maxX = _handles.Max(p => p.X);
        var minY = _handles.Min(p => p.Y);
        var maxY = _handles.Max(p => p.Y);

        var x0 = (int)Math.Floor(Math.Clamp(minX, 0, _imageWidth - 1));
        var y0 = (int)Math.Floor(Math.Clamp(minY, 0, _imageHeight - 1));
        var x1 = (int)Math.Ceiling(Math.Clamp(maxX, x0 + 1, _imageWidth));
        var y1 = (int)Math.Ceiling(Math.Clamp(maxY, y0 + 1, _imageHeight));

        var w = Math.Max(1, x1 - x0);
        var h = Math.Max(1, y1 - y0);

        _status = "Cropping...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = CurrentBytes!;
        byte[] cropped;
        try
        {
            cropped = await Task.Run(() => _imageProcessor.Crop(baseBytes, x0, y0, w, h));
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        _cropMode = false;
        _handles = new();
        CommitHistory(cropped, "Crop");

        _status = "Crop applied";
        RefreshFooter();
        NotifyAll();

        await ApplyPipelineDebouncedAsync();
    }

    public async Task OnStrokeCommittedAsync(CanvasStroke stroke)
    {
        if (!HasImage)
            return;

        if (_perspectiveMode)
            return;

        if (_cropMode)
            return;

        if (stroke.Points is null || stroke.Points.Count < 1)
            return;

        _status = "Drawing...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = CurrentBytes!;
        byte[] next;
        try
        {
            var radius = Math.Clamp(_brushRadius, 1, 64);
            next = await Task.Run(() => _imageProcessor.ApplyStroke(baseBytes, stroke.Mode, stroke.Points, radius));
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        CommitHistory(next, stroke.Mode == CanvasInteractionMode.Eraser ? "Eraser" : "Brush");

        _status = "Stroke applied";
        RefreshFooter();
        NotifyAll();

        await ApplyPipelineDebouncedAsync();
    }

    public async Task ApplyPerspectiveAsync()
    {
        if (!HasImage)
            return;

        var src = _handles.Count == 4
            ? _handles.Select(p => new Point2f((float)p.X, (float)p.Y)).ToArray()
            : throw new InvalidOperationException("Expected 4 perspective points.");

        var dst = new[]
        {
            new Point2f(0, 0),
            new Point2f(_imageWidth, 0),
            new Point2f(_imageWidth, _imageHeight),
            new Point2f(0, _imageHeight)
        };

        _status = "Applying perspective...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = CurrentBytes!;
        byte[] warped;
        try
        {
            warped = await Task.Run(() => _imageProcessor.WarpPerspective(baseBytes, src, dst, _imageWidth, _imageHeight));
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        _perspectiveMode = false;
        CommitHistory(warped, "Perspective");

        _status = "Perspective applied";
        RefreshFooter();
        NotifyAll();

        await ApplyPipelineDebouncedAsync();
    }

    public Task RotateLeftAsync()
        => ApplyTransformAsync(bytes => _imageProcessor.Rotate90(bytes, RotateFlags.Rotate90Counterclockwise), "Rotating...", "Rotate Left");

    public Task RotateRightAsync()
        => ApplyTransformAsync(bytes => _imageProcessor.Rotate90(bytes, RotateFlags.Rotate90Clockwise), "Rotating...", "Rotate Right");

    public Task ResizeHalfAsync()
        => ApplyTransformAsync(bytes => _imageProcessor.ResizeByScale(bytes, 0.5), "Resizing...", "Resize 50%");

    private async Task ApplyTransformAsync(Func<byte[], byte[]> transform, string inProgressStatus, string label)
    {
        if (!HasImage)
            return;

        _perspectiveMode = false;
        _status = inProgressStatus;
        RefreshFooter();
        NotifyAll();

        var baseBytes = CurrentBytes!;
        byte[] transformed;
        try
        {
            transformed = await Task.Run(() => transform(baseBytes));
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        CommitHistory(transformed, label);

        _status = "Transform applied";
        RefreshFooter();
        NotifyAll();

        await ApplyPipelineDebouncedAsync();
    }

    public Task OnBlurChanged(int v) { _blurKernelSize = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnGrayscaleChanged(bool v) { _grayscale = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnSepiaChanged(bool v) { _sepia = v; if (v) _grayscale = false; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnCannyChanged(bool v) { _canny = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnCannyT1Changed(double v) { _cannyT1 = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnCannyT2Changed(double v) { _cannyT2 = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnContrastChanged(double v) { _contrast = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnBrightnessChanged(double v) { _brightness = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }

    private Task ApplyPipelineDebouncedAsync()
    {
        if (!HasImage)
        {
            _viewBytes = null;
            _status = null;
            RefreshFooter();
            NotifyAll();
            return Task.CompletedTask;
        }

        if (_perspectiveMode)
            return Task.CompletedTask;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);

                var baseBytes = CurrentBytes!;
                var settings = new ImageProcessorService.ProcessingSettings(
                    BlurKernelSize: _blurKernelSize,
                    Grayscale: _grayscale,
                    Sepia: _sepia,
                    Canny: _canny,
                    CannyThreshold1: _cannyT1,
                    CannyThreshold2: _cannyT2,
                    Contrast: _contrast,
                    Brightness: _brightness);

                var processed = await Task.Run(() => _imageProcessor.ApplyPipeline(baseBytes, settings), token);

                if (token.IsCancellationRequested)
                    return;

                CommitHistory(processed, "Filters", replaceCurrentIfSameLabel: true, preserveHandles: _cropMode || _selectionMode);
                _status = "Updated";
                RefreshFooter();
                NotifyAll();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _status = ex.Message;
                RefreshFooter();
                NotifyAll();
            }
        }, token);

        return Task.CompletedTask;
    }

    private void CommitHistory(byte[] bytes, string label, bool replaceCurrentIfSameLabel = false, bool preserveHandles = false)
    {
        if (!HasImage)
            return;

        if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        var thumb = _imageProcessor.CreateThumbnailDataUrl(bytes);

        if (replaceCurrentIfSameLabel && _historyIndex >= 0 && _historyIndex == _history.Count - 1 && _history[_historyIndex].Label == label)
        {
            _history[_historyIndex] = new HistoryEntry(bytes, label, DateTime.UtcNow, thumb);
        }
        else
        {
            _history.Add(new HistoryEntry(bytes, label, DateTime.UtcNow, thumb));
            _historyIndex = _history.Count - 1;

            if (_history.Count > MaxHistoryEntries)
            {
                var remove = _history.Count - MaxHistoryEntries;
                _history.RemoveRange(0, remove);
                _historyIndex = Math.Max(0, _historyIndex - remove);
            }
        }

        _viewBytes = bytes;
        (_imageWidth, _imageHeight) = _imageProcessor.GetSize(bytes);

        if (!preserveHandles)
            _handles = new();
    }

    public Task UndoAsync() => GoToHistoryAsync(_historyIndex - 1);
    public Task RedoAsync() => GoToHistoryAsync(_historyIndex + 1);
    public Task ResetToOriginalAsync() => GoToHistoryAsync(0);

    public Task GoToHistoryAsync(int index)
    {
        if (!HasImage)
            return Task.CompletedTask;
        if (index < 0 || index >= _history.Count)
            return Task.CompletedTask;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _perspectiveMode = false;
        _historyIndex = index;

        var bytes = CurrentBytes;
        _viewBytes = bytes;
        (_imageWidth, _imageHeight) = _imageProcessor.GetSize(bytes);
        _handles = new();

        _status = "History restored";
        RefreshFooter();
        NotifyAll();
        return Task.CompletedTask;
    }

    public async Task OnLayoutUndoRequestedAsync()
    {
        if (!CanUndo)
            return;

        await UndoAsync();
    }

    public async Task OnLayoutRedoRequestedAsync()
    {
        if (!CanRedo)
            return;

        await RedoAsync();
    }

    public void Dispose()
    {
        _document.Changed -= OnDocChanged;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
