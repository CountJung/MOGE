using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SharedUI.Components;
using SharedUI.Mvvm;
using SharedUI.Logging;
using SharedUI.Services;

namespace SharedUI.ViewModels;

public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    private readonly IImageFilePicker _imageFilePicker;
    private readonly ImageDocumentState _document;
    private readonly ImageProcessorService _imageProcessor;
    private readonly IImageExportService _imageExport;
    private readonly MogeLogService _log;

    private Action<string?> _pushFooterMessage;

    private bool _layoutShortcutsSubscribed;

    private byte[]? _viewBytes;
    private ElementReference _canvas;
    private bool _hasCanvas;

    private const int MaxLayerHistoryEntries = 30;
    private sealed record LayerHistoryEntry(byte[] Bytes, string Label, DateTime Timestamp, string? ThumbnailDataUrl);

    private bool _perspectiveMode;
    private bool _cropMode;
    private bool _selectionMode;
    private List<CanvasPoint> _handles = new();
    private int _imageWidth;
    private int _imageHeight;

    private CanvasInteractionMode _interactionMode = CanvasInteractionMode.PanZoom;
    private int _brushRadius = 8;

    private int _magicWandTolerance = 30;
    private (int X, int Y)? _magicWandLastPoint;
    private Rgba32? _magicWandLastTarget;
    private CancellationTokenSource? _magicWandDebounceCts;

    private string _foregroundColorHex = "#000000";
    private string _backgroundColorHex = "#ffffff";
    private int _foregroundAlpha = 255;
    private int _backgroundAlpha = 255;
    private string _textInput = string.Empty;
    private int _textSize = 1;
    private int _textThickness = 2;
    private byte[]? _selectionMask;
    private List<CanvasPoint> _selectionPreviewHandles = new();
    private List<CanvasPoint> _selectionPreviewPolygonPoints = new();

    private int _selectionBlurKernelSize;
    private double _selectionSharpenAmount = 1.0;

    private int _blurKernelSize;
    private bool _grayscale;
    private bool _sepia;
    private bool _invert;
    private double _saturation = 1.0;
    private bool _sketch;
    private bool _cartoon;
    private bool _emboss;
    private double _filterSharpenAmount;
    private double _glowStrength;
    private ColorMapStyle _colorMap = ColorMapStyle.None;
    private double _colorMapStrength = 1.0;
    private int _posterizeLevels;
    private int _pixelizeBlockSize;
    private double _vignetteStrength;
    private double _noiseAmount;
    private bool _canny;
    private double _cannyT1 = 50;
    private double _cannyT2 = 150;
    private double _contrast = 1.0;
    private double _brightness;

    private CancellationTokenSource? _debounceCts;
    private string? _status;

    private int _processingCount;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    /// <summary>
    /// Callback to request text input from UI (invoked from Text tool on canvas click).
    /// Parameters: (initialText, fontSize, thickness, colorHex, alpha) -> (text, fontSize, thickness, colorHex, alpha)? or null if canceled.
    /// </summary>
    private Func<string, int, int, string, int, Task<(string Text, int FontSize, int Thickness, string ColorHex, int Alpha)?>>? _requestTextInput;

    private const int FilterPreviewMaxSide = 1200;
    private const int FilterPreviewThreshold = 1600;

    private sealed record LoadedImage(ImagePickResult Pick, string? ThumbnailDataUrl);
    private readonly List<LoadedImage> _loadedImages = new();
    private int _selectedLoadedIndex = -1;

    public EditorViewModel(
        IImageFilePicker imageFilePicker,
        ImageDocumentState document,
        ImageProcessorService imageProcessor,
        IImageExportService imageExport,
        MogeLogService log,
        Action<string?>? pushFooterMessage = null)
    {
        _imageFilePicker = imageFilePicker;
        _document = document;
        _imageProcessor = imageProcessor;
        _imageExport = imageExport;
        _log = log;
        _pushFooterMessage = pushFooterMessage ?? (_ => { });
    }

    public bool HasImage => _document.HasImage;
    public string? FileName => _document.FileName;
    public long FileSizeBytes => _document.Bytes?.Length ?? 0;
    public string? ContentType => _document.ContentType;

    public int ImageWidth => _imageWidth;
    public int ImageHeight => _imageHeight;

    public byte[]? ViewBytes => _viewBytes;

    public bool IsProcessing => Volatile.Read(ref _processingCount) > 0;

    public bool PerspectiveMode => _perspectiveMode;
    public bool CropMode => _cropMode;
    public bool SelectionMode => _selectionMode;

    public CanvasInteractionMode InteractionMode => _interactionMode;
    public int BrushRadius => _brushRadius;

    public int MagicWandTolerance => _magicWandTolerance;

    public string ForegroundColorHex => _foregroundColorHex;
    public string BackgroundColorHex => _backgroundColorHex;

    public int ForegroundAlpha => _foregroundAlpha;
    public int BackgroundAlpha => _backgroundAlpha;

    public string TextInput => _textInput;

    public int TextSize => _textSize;
    public int TextThickness => _textThickness;

    public bool HasSelectionPreview => _selectionPreviewHandles.Count == 4;
    public IReadOnlyList<CanvasPoint> SelectionPreviewHandles => _selectionPreviewHandles;

    public bool HasSelectionPreviewPolygon => _selectionPreviewPolygonPoints.Count > 2;
    public IReadOnlyList<CanvasPoint> SelectionPreviewPolygonPoints => _selectionPreviewPolygonPoints;

    public bool CanFillSelection => HasImage && (_selectionMode || _selectionMask is { Length: > 0 });

    public IReadOnlyList<CanvasPoint> Handles => _handles;

    public int SelectionBlurKernelSize => _selectionBlurKernelSize;
    public double SelectionSharpenAmount => _selectionSharpenAmount;

    public int BlurKernelSize => _blurKernelSize;
    public bool Grayscale => _grayscale;
    public bool Sepia => _sepia;
    public bool Invert => _invert;
    public double Saturation => _saturation;
    public bool Sketch => _sketch;
    public bool Cartoon => _cartoon;
    public bool Emboss => _emboss;
    public double FilterSharpenAmount => _filterSharpenAmount;
    public double GlowStrength => _glowStrength;
    public ColorMapStyle ColorMap => _colorMap;
    public double ColorMapStrength => _colorMapStrength;
    public int PosterizeLevels => _posterizeLevels;
    public int PixelizeBlockSize => _pixelizeBlockSize;
    public double VignetteStrength => _vignetteStrength;
    public double NoiseAmount => _noiseAmount;
    public bool Canny => _canny;
    public double CannyT1 => _cannyT1;
    public double CannyT2 => _cannyT2;
    public double Contrast => _contrast;
    public double Brightness => _brightness;

    public int SelectedLoadedIndex => _selectedLoadedIndex;

    public bool CanUndo => HasActiveLayer && GetActiveLayerHistoryIndex() > 0;
    public bool CanRedo
    {
        get
        {
            if (!HasActiveLayer) return false;
            var layer = GetActiveLayerInternal();
            if (layer is null) return false;
            return layer.HistoryIndex >= 0 && layer.HistoryIndex < layer.History.Count - 1;
        }
    }

    public int CurrentHistoryIndex => GetActiveLayerHistoryIndex();

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

    public IReadOnlyList<string> LoadedImageNames
    {
        get
        {
            if (_loadedImages.Count == 0)
                return Array.Empty<string>();

            var names = new string[_loadedImages.Count];
            for (var i = 0; i < _loadedImages.Count; i++)
                names[i] = _loadedImages[i].Pick.FileName;

            return names;
        }
    }

    private byte[]? CurrentBytes
    {
        get
        {
            // Prefer active layer bytes, fallback to document
            var layerBytes = ActiveLayerBytes;
            if (layerBytes is { Length: > 0 })
                return layerBytes;
            return _document.Bytes;
        }
    }

    public void SetFooterPusher(Action<string?>? pushFooterMessage)
        => _pushFooterMessage = pushFooterMessage ?? (_ => { });

    public void SetTextInputRequester(Func<string, int, int, string, int, Task<(string Text, int FontSize, int Thickness, string ColorHex, int Alpha)?>>? requester)
        => _requestTextInput = requester;

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

        _blurKernelSize = 0;
        _grayscale = false;
        _sepia = false;
        _invert = false;
        _saturation = 1.0;
        _sketch = false;
        _cartoon = false;
        _emboss = false;
        _filterSharpenAmount = 0.0;
        _glowStrength = 0.0;
        _colorMap = ColorMapStyle.None;
        _posterizeLevels = 0;
        _pixelizeBlockSize = 0;
        _vignetteStrength = 0.0;
        _noiseAmount = 0.0;
        _canny = false;
        _cannyT1 = 50;
        _cannyT2 = 150;
        _contrast = 1.0;
        _brightness = 0;

        LayersReset();

        if (_document.Bytes is { Length: > 0 } bytes)
        {
            // Initialize layers from document bytes (includes initial history)
            LayersInitFromDocumentBytes(bytes);
            _ = UpdateInitialImageMetaAsync(bytes);
        }

        _viewBytes ??= GetCompositedBytesOrFallback() ?? CurrentBytes;
        _handles = new();
        _selectionMask = null;
        _imageWidth = 0;
        _imageHeight = 0;
        _status = null;
    }

    private async Task UpdateInitialImageMetaAsync(byte[] bytes)
    {
        try
        {
            var (thumb, size) = await RunImageCpuAsync(
                () => (_imageProcessor.CreateThumbnailDataUrl(bytes), _imageProcessor.GetSize(bytes)),
                inProgressStatus: "Loading...");

            if (!HasImage)
                return;

            // Only update if the current image still matches.
            if (CurrentBytes is not { Length: > 0 } current || !ReferenceEquals(current, bytes))
                return;

            // Update the first history entry's thumbnail for the active layer
            var layer = GetActiveLayerInternal();
            if (layer is not null && layer.History.Count > 0 && ReferenceEquals(layer.History[0].Bytes, bytes))
            {
                var old = layer.History[0];
                layer.History[0] = new LayerHistoryEntry(old.Bytes, old.Label, old.Timestamp, thumb);
            }

            (_imageWidth, _imageHeight) = size;
            NotifyAll();
        }
        catch
        {
            // Best-effort only.
        }
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
            try
            {
                thumb = await RunImageCpuAsync(() => _imageProcessor.CreateThumbnailDataUrl(pick.Bytes), inProgressStatus: "Loading...");
            }
            catch
            {
            }
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

    public Task NewAsync()
    {
        // Reset editor state but keep the loaded images list (acts like "new document" on current selection).
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _magicWandDebounceCts?.Cancel();
        _magicWandDebounceCts?.Dispose();
        _magicWandDebounceCts = null;

        _perspectiveMode = false;
        _cropMode = false;
        _selectionMode = false;
        _handles = new();
        _selectionMask = null;
        _selectionPreviewHandles = new();
        _selectionPreviewPolygonPoints = new();

        // Reset all layer histories
        foreach (var layer in _layers)
        {
            layer.History.Clear();
            layer.HistoryIndex = -1;
        }

        _viewBytes = CurrentBytes;
        _status = null;

        NotifyAll();
        return Task.CompletedTask;
    }

    public Task CreateNewCanvasAsync(int width, int height)
    {
        var created = _imageProcessor.CreateBlankWhite(width, height);

        // Starting a brand-new canvas: clear any loaded list selection.
        _loadedImages.Clear();
        _selectedLoadedIndex = -1;

        var safeW = Math.Clamp(width, 1, 8192);
        var safeH = Math.Clamp(height, 1, 8192);
        _imageWidth = safeW;
        _imageHeight = safeH;

        var fileName = $"canvas-{safeW}x{safeH}.png";
        _document.Set(new ImagePickResult(fileName, created.ContentType, created.Bytes));

        // Reset editor state and show the new image.
        _ = NewAsync();
        _viewBytes = created.Bytes;
        _status = $"New canvas: {safeW}x{safeH}";
        RefreshFooter();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task RemoveSelectedLoadedImageAsync()
    {
        if (_selectedLoadedIndex < 0 || _selectedLoadedIndex >= _loadedImages.Count)
            return Task.CompletedTask;

        var removingCurrent = HasImage;

        _loadedImages.RemoveAt(_selectedLoadedIndex);

        if (_loadedImages.Count == 0)
        {
            _selectedLoadedIndex = -1;
            _document.Clear();
            _viewBytes = null;
            LayersReset();
            _handles = new();
            _selectionMask = null;
            _selectionPreviewHandles = new();
            _selectionPreviewPolygonPoints = new();
            _status = null;
            NotifyAll();
            return Task.CompletedTask;
        }

        // Re-select: prefer same index, otherwise previous.
        var nextIndex = Math.Min(_selectedLoadedIndex, _loadedImages.Count - 1);
        if (nextIndex < 0)
            nextIndex = 0;

        _selectedLoadedIndex = -1;

        // Switching source should reset editor state.
        if (removingCurrent)
            _ = NewAsync();

        SelectLoadedImage(nextIndex);
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
        await SaveAsAsync($"{baseName}-edited", ImageExportFormat.Png);
        RefreshFooter();
        NotifyAll();
    }

    public async Task SaveAsAsync(string fileName, ImageExportFormat format)
    {
        if (!HasImage)
            return;

        if (!_hasCanvas)
            return;

        BeginProcessing("Saving...");
        try
        {
            await _imageExport.SaveAsync(_canvas, fileName, format);
            var ext = format == ImageExportFormat.Jpeg ? ".jpg" : ".png";
            _status = $"Saved: {fileName}{ext}";
            RefreshFooter();
            NotifyAll();
        }
        catch (Exception ex)
        {
            ReportError("저장 중 문제가 발생했습니다. 로그를 내보내기에서 확인해 주세요.", ex, "Save");
        }
        finally
        {
            EndProcessing();
        }
    }

    private void RefreshFooter() => _pushFooterMessage(_status);

    private void BeginProcessing(string? status = null)
    {
        Interlocked.Increment(ref _processingCount);

        if (!string.IsNullOrWhiteSpace(status))
        {
            _status = status;
            RefreshFooter();
        }

        NotifyAll();
    }

    private void EndProcessing()
    {
        Interlocked.Decrement(ref _processingCount);
        NotifyAll();
    }

    private async Task<T> RunImageCpuAsync<T>(Func<T> op, string? inProgressStatus = null, CancellationToken ct = default)
    {
        // Acquire lock to prevent concurrent image processing from left/right panels
        await _processingLock.WaitAsync(ct);
        BeginProcessing(inProgressStatus);
        try
        {
            return await Task.Run(op, ct);
        }
        finally
        {
            EndProcessing();
            _processingLock.Release();
        }
    }

    private IReadOnlyList<EditorHistoryListItem> GetHistoryItems()
    {
        var layer = GetActiveLayerInternal();
        if (layer is null || layer.History.Count == 0)
            return Array.Empty<EditorHistoryListItem>();

        var items = new List<EditorHistoryListItem>(layer.History.Count);
        for (var i = 0; i < layer.History.Count; i++)
            items.Add(new EditorHistoryListItem(i, layer.History[i].Label, layer.History[i].ThumbnailDataUrl));

        return items;
    }

    public async Task OnPerspectiveModeChanged(bool enabled)
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
            return;
        }

        if (!HasImage)
        {
            _perspectiveMode = false;
            RefreshFooter();
            NotifyAll();
            return;
        }

        var baseBytes = CurrentBytes;
        _viewBytes = baseBytes;

        (_imageWidth, _imageHeight) = await RunImageCpuAsync(() => _imageProcessor.GetSize(baseBytes), inProgressStatus: "Preparing...");
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

        return;
    }

    public async Task OnCropModeChanged(bool enabled)
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
                return;
            }

            var baseBytes = CurrentBytes;
            _viewBytes = baseBytes;

            (_imageWidth, _imageHeight) = await RunImageCpuAsync(() => _imageProcessor.GetSize(baseBytes), inProgressStatus: "Preparing...");

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
            return;
        }

        _handles = new();
        _status = null;
        RefreshFooter();
        NotifyAll();
        _ = ApplyPipelineDebouncedAsync();
        return;
    }

    public async Task OnSelectionModeChanged(bool enabled)
    {
        _selectionMode = enabled;
        _selectionMask = null;
        _selectionPreviewHandles = new();

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
                return;
            }

            var baseBytes = CurrentBytes;
            _viewBytes = baseBytes;

            (_imageWidth, _imageHeight) = await RunImageCpuAsync(() => _imageProcessor.GetSize(baseBytes), inProgressStatus: "Preparing...");

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
            return;
        }

        _handles = new();
        _status = null;
        RefreshFooter();
        NotifyAll();
        return;
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

        if (mode != CanvasInteractionMode.PanZoom)
            _selectionMask = null;

        if (mode != CanvasInteractionMode.MagicWand)
        {
            _selectionPreviewHandles = new();
            _selectionPreviewPolygonPoints = new();
            _magicWandLastPoint = null;
            _magicWandLastTarget = null;
        }

        _status = mode == CanvasInteractionMode.PanZoom ? null : $"Tool: {mode}";
        RefreshFooter();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnMagicWandToleranceChanged(int v)
    {
        _magicWandTolerance = Math.Clamp(v, 0, 255);
        NotifyAll();

        if (_interactionMode == CanvasInteractionMode.MagicWand && _magicWandLastPoint is not null)
            _ = RecomputeMagicWandSelectionDebouncedAsync();

        return Task.CompletedTask;
    }

    public async Task OnCanvasClickedAsync(CanvasPoint p)
    {
        if (!HasImage)
            return;

        var x = (int)Math.Round(p.X);
        var y = (int)Math.Round(p.Y);

        x = Math.Clamp(x, 0, Math.Max(0, _imageWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, _imageHeight - 1));

        if (_interactionMode == CanvasInteractionMode.MagicWand)
        {
            await RunMagicWandAtAsync(x, y, targetOverride: null, CancellationToken.None);
            return;
        }

        if (_interactionMode == CanvasInteractionMode.Text)
        {
            await ApplyTextAtAsync(x, y);
            return;
        }
    }

    private async Task RecomputeMagicWandSelectionDebouncedAsync()
    {
        _magicWandDebounceCts?.Cancel();
        _magicWandDebounceCts?.Dispose();

        var cts = new CancellationTokenSource();
        _magicWandDebounceCts = cts;

        try
        {
            await Task.Delay(150, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_magicWandLastPoint is null)
            return;

        await RunMagicWandAtAsync(_magicWandLastPoint.Value.X, _magicWandLastPoint.Value.Y, targetOverride: _magicWandLastTarget, cts.Token);
    }

    private async Task RunMagicWandAtAsync(int x, int y, Rgba32? targetOverride, CancellationToken ct)
    {
        if (!HasImage)
            return;

        var baseBytes = CurrentBytes;
        if (baseBytes is null)
            return;

        var target = targetOverride ?? _imageProcessor.GetPixelColor(baseBytes, x, y);

        _magicWandLastPoint = (x, y);
        _magicWandLastTarget = target;

        _status = "Magic wand: selecting...";
        RefreshFooter();
        NotifyAll();

        try
        {
            var iw = Math.Max(1, _imageWidth);
            var ih = Math.Max(1, _imageHeight);

            var mask = await Task.Run(
                () => _imageProcessor.CreateConnectedSimilarColorMask(baseBytes, x, y, target, tolerance: _magicWandTolerance),
                ct);

            if (ct.IsCancellationRequested)
                return;

            _selectionMask = mask;
            _selectionPreviewHandles = ComputeMaskBoundingRectHandles(mask, _imageWidth, _imageHeight);
            _selectionPreviewPolygonPoints = ComputeMaskOutlinePolygonPoints(mask, _imageWidth, _imageHeight);
            _status = "Magic wand: selected";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            _selectionMask = null;
            _selectionPreviewHandles = new();
            _selectionPreviewPolygonPoints = new();
            _selectionPreviewPolygonPoints = new();
        }

        RefreshFooter();
        NotifyAll();
    }

    private async Task ApplyTextAtAsync(int x, int y)
    {
        if (!HasImage)
            return;

        // Request text input from UI via dialog callback
        if (_requestTextInput is null)
        {
            // Fallback: use pre-entered text if dialog not available (e.g., tests)
            if (string.IsNullOrWhiteSpace(_textInput))
            {
                _status = "Text is empty";
                RefreshFooter();
                NotifyAll();
                return;
            }
        }
        else
        {
            // Show dialog to input text
            var inputResult = await _requestTextInput(
                _textInput,
                _textSize,
                _textThickness,
                _foregroundColorHex,
                _foregroundAlpha);

            if (inputResult is null)
            {
                // User canceled
                return;
            }

            // Update text settings from dialog result
            _textInput = inputResult.Value.Text;
            _textSize = inputResult.Value.FontSize;
            _textThickness = inputResult.Value.Thickness;
            _foregroundColorHex = inputResult.Value.ColorHex;
            _foregroundAlpha = inputResult.Value.Alpha;
        }

        if (string.IsNullOrWhiteSpace(_textInput))
        {
            _status = "Text is empty";
            RefreshFooter();
            NotifyAll();
            return;
        }

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();

        var color = WithAlpha(Rgba32.FromHexOrDefault(_foregroundColorHex, new Rgba32(0, 0, 0, 255)), _foregroundAlpha);

        _status = "Applying text...";
        RefreshFooter();
        NotifyAll();

        byte[] next;
        try
        {
            var scale = (double)Math.Clamp(_textSize, 1, 8);
            var thickness = Math.Clamp(_textThickness, 1, 6);
            next = await RunImageCpuAsync(
                () => _imageProcessor.DrawText(baseBytes, _textInput, x, y, color, scale, thickness),
                inProgressStatus: "Applying text...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(next);
        await CommitHistoryAsync(_viewBytes ?? next, "Text", preserveHandles: true);
        _status = "Text applied";
        RefreshFooter();
        NotifyAll();
        await ApplyPipelineDebouncedAsync();
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
            // Keep handle ordering stable (TL/TR/BR/BL) during dragging.
            // Normalizing to min/max each move can swap indices, which makes the active drag handle
            // "jump" and feels like the rectangle stops shrinking.
            _handles = updated
                .Select(p => new CanvasPoint(
                    Math.Clamp(p.X, 0, _imageWidth),
                    Math.Clamp(p.Y, 0, _imageHeight)))
                .ToList();

            _selectionMask = null;
            _selectionPreviewHandles = new();

            NotifyAll();
            return Task.CompletedTask;
        }

        _handles = updated.ToList();
        _selectionMask = null;
        _selectionPreviewHandles = new();
        _selectionPreviewPolygonPoints = new();
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnForegroundColorHexChanged(string hex)
    {
        _foregroundColorHex = NormalizeHexColor(hex, "#000000");
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnForegroundAlphaChanged(int a)
    {
        _foregroundAlpha = Math.Clamp(a, 0, 255);
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnBackgroundColorHexChanged(string hex)
    {
        _backgroundColorHex = NormalizeHexColor(hex, "#ffffff");
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnBackgroundAlphaChanged(int a)
    {
        _backgroundAlpha = Math.Clamp(a, 0, 255);
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnTextInputChanged(string text)
    {
        _textInput = text ?? string.Empty;
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnTextSizeChanged(int size)
    {
        _textSize = Math.Clamp(size, 1, 8);
        NotifyAll();
        return Task.CompletedTask;
    }

    public Task OnTextThicknessChanged(int thickness)
    {
        _textThickness = Math.Clamp(thickness, 1, 6);
        NotifyAll();
        return Task.CompletedTask;
    }

    public async Task SelectSimilarColorsAsync()
    {
        if (!HasImage)
            return;

        int x0;
        int y0;
        int w;
        int h;

        if (_selectionMode)
        {
            if (_handles.Count != 4)
            {
                _status = "Selection: expected 4 points";
                RefreshFooter();
                NotifyAll();
                return;
            }

            (x0, y0, w, h) = GetHandlesRect();
        }
        else
        {
            x0 = 0;
            y0 = 0;
            w = Math.Max(1, _imageWidth);
            h = Math.Max(1, _imageHeight);
        }

        var target = WithAlpha(Rgba32.FromHexOrDefault(_foregroundColorHex, new Rgba32(0, 0, 0, 255)), _foregroundAlpha);

        _status = "Selecting similar colors...";
        RefreshFooter();
        NotifyAll();

        try
        {
            var baseBytes = CurrentBytes;
            if (baseBytes is null)
                return;

            _selectionMask = await RunImageCpuAsync(
                () => _imageProcessor.CreateSimilarColorMask(baseBytes, x0, y0, w, h, target, tolerance: _magicWandTolerance),
                inProgressStatus: "Selecting similar colors...");

            // When not in selection mode, show a dashed preview rectangle (bounding box of the mask).
            _selectionPreviewHandles = _selectionMode
                ? new()
                : ComputeMaskBoundingRectHandles(_selectionMask, _imageWidth, _imageHeight);

            _selectionPreviewPolygonPoints = _selectionMode
                ? new()
                : ComputeMaskOutlinePolygonPoints(_selectionMask, _imageWidth, _imageHeight);

            _status = "Similar colors selected";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            _selectionPreviewHandles = new();
            _selectionPreviewPolygonPoints = new();
        }

        RefreshFooter();
        NotifyAll();
    }

    public async Task FillSelectionAsync()
    {
        var fill = WithAlpha(Rgba32.FromHexOrDefault(_foregroundColorHex, new Rgba32(0, 0, 0, 255)), _foregroundAlpha);

        var mask = _selectionMask;

        if (_selectionMode)
        {
            if (_handles.Count != 4)
            {
                _status = "Selection: expected 4 points";
                RefreshFooter();
                NotifyAll();
                return;
            }

            if (mask is null)
            {
                // Fill the whole selection rectangle.
                var (x0, y0, w, h) = GetHandlesRect();
                mask = new byte[_imageWidth * _imageHeight];
                for (var yy = y0; yy < y0 + h; yy++)
                {
                    var row = yy * _imageWidth;
                    for (var xx = x0; xx < x0 + w; xx++)
                        mask[row + xx] = 255;
                }
            }
        }
        else
        {
            if (mask is null || mask.Length != _imageWidth * _imageHeight)
                return;
        }

        _status = "Filling selection...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] next;
        try
        {
            next = await RunImageCpuAsync(() => _imageProcessor.FillByMask(baseBytes, mask, fill), inProgressStatus: "Filling selection...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(next);
        await CommitHistoryAsync(_viewBytes ?? next, "Fill", preserveHandles: true);
        _status = "Fill applied";
        RefreshFooter();
        NotifyAll();
        await ApplyPipelineDebouncedAsync();
    }

    public async Task ApplyTextAsync()
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

        if (string.IsNullOrWhiteSpace(_textInput))
            return;

        var (x0, y0, w, h) = GetHandlesRect();
        var color = WithAlpha(Rgba32.FromHexOrDefault(_foregroundColorHex, new Rgba32(0, 0, 0, 255)), _foregroundAlpha);

        // Place text near the top-left inside the selection rectangle.
        var tx = Math.Clamp(x0 + 4, 0, Math.Max(0, _imageWidth - 1));
        var ty = Math.Clamp(y0 + Math.Min(24, Math.Max(12, h - 4)), 0, Math.Max(0, _imageHeight - 1));

        _status = "Applying text...";
        RefreshFooter();
        NotifyAll();

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] next;
        try
        {
            var scale = (double)Math.Clamp(_textSize, 1, 8);
            var thickness = Math.Clamp(_textThickness, 1, 6);
            next = await RunImageCpuAsync(
                () => _imageProcessor.DrawText(baseBytes, _textInput, tx, ty, color, scale, thickness),
                inProgressStatus: "Applying text...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(next);
        await CommitHistoryAsync(_viewBytes ?? next, "Text", preserveHandles: true);
        _status = "Text applied";
        RefreshFooter();
        NotifyAll();
        await ApplyPipelineDebouncedAsync();
    }

    private static string NormalizeHexColor(string? hex, string fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        var s = hex.Trim();
        if (!s.StartsWith('#'))
            s = "#" + s;

        if (s.Length != 7)
            return fallback;

        // basic validation
        for (var i = 1; i < 7; i++)
        {
            var c = s[i];
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok)
                return fallback;
        }

        return s;
    }

    private static Rgba32 WithAlpha(Rgba32 c, int a)
        => new(c.R, c.G, c.B, (byte)Math.Clamp(a, 0, 255));

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

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] next;
        try
        {
            next = await RunImageCpuAsync(
                () => _imageProcessor.BlurRegion(baseBytes, x0, y0, w, h, kernel),
                inProgressStatus: "Blurring selection...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(next);
        await CommitHistoryAsync(_viewBytes ?? next, "Blur", preserveHandles: true);
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

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] next;
        try
        {
            next = await RunImageCpuAsync(
                () => _imageProcessor.SharpenRegion(baseBytes, x0, y0, w, h, amount),
                inProgressStatus: "Sharpening selection...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(next);
        await CommitHistoryAsync(_viewBytes ?? next, "Sharpen", preserveHandles: true);
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

    private static List<CanvasPoint> ComputeMaskBoundingRectHandles(byte[]? mask, int width, int height)
    {
        if (mask is null || mask.Length == 0 || width <= 0 || height <= 0)
            return new();

        if (mask.Length != width * height)
            return new();

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (mask[row + x] == 0)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return new();

        // Convert inclusive bounds to edge coordinates.
        var left = Math.Clamp(minX, 0, width);
        var top = Math.Clamp(minY, 0, height);
        var right = Math.Clamp(maxX + 1, 0, width);
        var bottom = Math.Clamp(maxY + 1, 0, height);

        return new List<CanvasPoint>
        {
            new(left, top),
            new(right, top),
            new(right, bottom),
            new(left, bottom)
        };
    }

    private static List<CanvasPoint> ComputeMaskOutlinePolygonPoints(byte[]? mask, int width, int height)
    {
        if (mask is null || mask.Length == 0 || width <= 0 || height <= 0)
            return new();

        if (mask.Length != width * height)
            return new();

        var segments = new List<(IntPoint A, IntPoint B)>(capacity: 2048);

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (mask[row + x] == 0)
                    continue;

                var hasTop = y > 0 && mask[(y - 1) * width + x] != 0;
                var hasRight = x < width - 1 && mask[row + (x + 1)] != 0;
                var hasBottom = y < height - 1 && mask[(y + 1) * width + x] != 0;
                var hasLeft = x > 0 && mask[row + (x - 1)] != 0;

                // pixel square boundary on integer grid; clockwise segments
                if (!hasTop)
                    segments.Add((new IntPoint(x, y), new IntPoint(x + 1, y)));
                if (!hasRight)
                    segments.Add((new IntPoint(x + 1, y), new IntPoint(x + 1, y + 1)));
                if (!hasBottom)
                    segments.Add((new IntPoint(x + 1, y + 1), new IntPoint(x, y + 1)));
                if (!hasLeft)
                    segments.Add((new IntPoint(x, y + 1), new IntPoint(x, y)));
            }
        }

        if (segments.Count == 0)
            return new();

        var outgoing = new Dictionary<IntPoint, List<IntPoint>>(capacity: segments.Count);
        var unused = new HashSet<Seg>(capacity: segments.Count);

        foreach (var (a, b) in segments)
        {
            if (!outgoing.TryGetValue(a, out var list))
            {
                list = new List<IntPoint>(1);
                outgoing[a] = list;
            }

            list.Add(b);
            unused.Add(new Seg(a, b));
        }

        List<IntPoint>? best = null;

        foreach (var s in unused.ToArray())
        {
            if (!unused.Contains(s))
                continue;

            var start = s.A;
            var loop = new List<IntPoint>(capacity: 256) { s.A, s.B };
            unused.Remove(s);

            var next = s.B;
            var guard = 0;
            while (!next.Equals(start) && guard++ < 1_000_000)
            {
                if (!outgoing.TryGetValue(next, out var outs) || outs.Count == 0)
                    break;

                Seg? found = null;
                for (var i = 0; i < outs.Count; i++)
                {
                    var cand = new Seg(next, outs[i]);
                    if (unused.Contains(cand))
                    {
                        found = cand;
                        break;
                    }
                }

                if (found is null)
                    break;

                unused.Remove(found.Value);
                next = found.Value.B;
                loop.Add(next);
            }

            if (loop.Count >= 4 && loop[^1].Equals(start))
            {
                if (best is null || loop.Count > best.Count)
                    best = loop;
            }
        }

        if (best is null)
            return new();

        if (best.Count > 1 && best[^1].Equals(best[0]))
            best.RemoveAt(best.Count - 1);

        var result = new List<CanvasPoint>(best.Count);
        for (var i = 0; i < best.Count; i++)
            result.Add(new CanvasPoint(best[i].X, best[i].Y));
        return result;
    }

    private readonly record struct IntPoint(int X, int Y);
    private readonly record struct Seg(IntPoint A, IntPoint B);

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

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] cropped;
        try
        {
            cropped = await RunImageCpuAsync(() => _imageProcessor.Crop(baseBytes, x0, y0, w, h), inProgressStatus: "Cropping...");
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
        ApplyToActiveLayerAndRefresh(cropped);
        await CommitHistoryAsync(_viewBytes ?? cropped, "Crop");

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

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] next;
        try
        {
            var radius = Math.Clamp(_brushRadius, 1, 64);
            var color = stroke.Mode == CanvasInteractionMode.Eraser
                ? WithAlpha(Rgba32.FromHexOrDefault(_backgroundColorHex, new Rgba32(255, 255, 255, 255)), _backgroundAlpha)
                : WithAlpha(Rgba32.FromHexOrDefault(_foregroundColorHex, new Rgba32(0, 0, 0, 255)), _foregroundAlpha);

            next = await RunImageCpuAsync(
                () => _imageProcessor.ApplyStroke(baseBytes, stroke.Mode, stroke.Points, radius, color),
                inProgressStatus: "Applying stroke...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(next);
        await CommitHistoryAsync(_viewBytes ?? next, stroke.Mode == CanvasInteractionMode.Eraser ? "Eraser" : "Brush");
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

        var baseBytes = GetActiveLayerOrCurrentBytesOrThrow();
        byte[] warped;
        try
        {
            warped = await RunImageCpuAsync(
                () => _imageProcessor.WarpPerspective(baseBytes, src, dst, _imageWidth, _imageHeight),
                inProgressStatus: "Warping...");
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        _perspectiveMode = false;
        ApplyToActiveLayerAndRefresh(warped);
        await CommitHistoryAsync(_viewBytes ?? warped, "Perspective");

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

        var baseBytes = GetCompositedBytesOrFallback() ?? CurrentBytes;
        byte[] transformed;
        try
        {
            transformed = await RunImageCpuAsync(() => transform(baseBytes!), inProgressStatus: inProgressStatus);
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            RefreshFooter();
            NotifyAll();
            return;
        }

        ApplyToActiveLayerAndRefresh(transformed);
        await CommitHistoryAsync(_viewBytes ?? transformed, label);

        _status = "Transform applied";
        RefreshFooter();
        NotifyAll();

        await ApplyPipelineDebouncedAsync();
    }

    public Task OnBlurChanged(int v) { _blurKernelSize = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnGrayscaleChanged(bool v)
    {
        _grayscale = v;
        if (v)
        {
            _sepia = false;
            _sketch = false;
            _cartoon = false;
            _colorMap = ColorMapStyle.None;
        }
        NotifyAll();
        return ApplyPipelineDebouncedAsync();
    }

    public Task OnSepiaChanged(bool v)
    {
        _sepia = v;
        if (v)
        {
            _grayscale = false;
            _sketch = false;
            _cartoon = false;
            _colorMap = ColorMapStyle.None;
        }
        NotifyAll();
        return ApplyPipelineDebouncedAsync();
    }

    public Task OnInvertChanged(bool v) { _invert = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnSaturationChanged(double v) { _saturation = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }

    public Task OnSketchChanged(bool v)
    {
        _sketch = v;
        if (v)
        {
            _cartoon = false;
            _grayscale = false;
            _sepia = false;
            _colorMap = ColorMapStyle.None;
        }
        NotifyAll();
        return ApplyPipelineDebouncedAsync();
    }

    public Task OnCartoonChanged(bool v)
    {
        _cartoon = v;
        if (v)
        {
            _sketch = false;
            _grayscale = false;
            _sepia = false;
            _colorMap = ColorMapStyle.None;
        }
        NotifyAll();
        return ApplyPipelineDebouncedAsync();
    }

    public Task OnEmbossChanged(bool v) { _emboss = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnFilterSharpenAmountChanged(double v) { _filterSharpenAmount = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnGlowStrengthChanged(double v) { _glowStrength = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }

    public Task OnColorMapChanged(ColorMapStyle v)
    {
        _colorMap = v;
        if (v != ColorMapStyle.None)
        {
            _grayscale = false;
            _sepia = false;
            _sketch = false;
            _cartoon = false;
            // When selecting a color map, set strength to 1.0 if it was 0
            if (_colorMapStrength < 0.01)
                _colorMapStrength = 1.0;
        }
        NotifyAll();
        return ApplyPipelineDebouncedAsync();
    }

    public Task OnColorMapStrengthChanged(double v)
    {
        _colorMapStrength = v;
        NotifyAll();
        return ApplyPipelineDebouncedAsync();
    }

    public Task OnPosterizeLevelsChanged(int v) { _posterizeLevels = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnPixelizeBlockSizeChanged(int v) { _pixelizeBlockSize = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnVignetteStrengthChanged(double v) { _vignetteStrength = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
    public Task OnNoiseAmountChanged(double v) { _noiseAmount = v; NotifyAll(); return ApplyPipelineDebouncedAsync(); }
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

        _ = ApplyPipelineAsync(token);

        return Task.CompletedTask;
    }

    private ImageProcessorService.ProcessingSettings BuildProcessingSettings()
        => new(
            BlurKernelSize: _blurKernelSize,
            Grayscale: _grayscale,
            Sepia: _sepia,
            Invert: _invert,
            Saturation: _saturation,
            Sketch: _sketch,
            Cartoon: _cartoon,
            Emboss: _emboss,
            SharpenAmount: _filterSharpenAmount,
            GlowStrength: _glowStrength,
            ColorMap: _colorMap,
            ColorMapStrength: _colorMapStrength,
            PosterizeLevels: _posterizeLevels,
            PixelizeBlockSize: _pixelizeBlockSize,
            VignetteStrength: _vignetteStrength,
            NoiseAmount: _noiseAmount,
            Canny: _canny,
            CannyThreshold1: _cannyT1,
            CannyThreshold2: _cannyT2,
            Contrast: _contrast,
            Brightness: _brightness);

    private bool ShouldUseFilterPreview()
    {
        if (_imageWidth <= 0 || _imageHeight <= 0)
            return false;

        var maxSide = Math.Max(_imageWidth, _imageHeight);
        return maxSide >= FilterPreviewThreshold;
    }

    private async Task ApplyPipelineAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);

            var baseBytes = GetCompositedBytesOrFallback() ?? CurrentBytes!;
            var settings = BuildProcessingSettings();

            var usePreview = ShouldUseFilterPreview();
            if (usePreview)
            {
                var preview = await RunImageCpuAsync(
                    () => _imageProcessor.ApplyPipelinePreview(baseBytes, settings, FilterPreviewMaxSide),
                    inProgressStatus: "Preview...",
                    ct: token);

                if (token.IsCancellationRequested || !HasImage)
                    return;

                _viewBytes = preview;
                _status = "Preview";
                RefreshFooter();
                NotifyAll();

                await Task.Delay(350, token);
            }

            var processed = await RunImageCpuAsync(
                () => _imageProcessor.ApplyPipeline(baseBytes, settings),
                inProgressStatus: "Processing...",
                ct: token);

            if (token.IsCancellationRequested)
                return;

            // Apply the processed result to the active layer (or all if only one layer exists)
            ApplyToActiveLayerAndRefresh(processed);
            await CommitHistoryAsync(_viewBytes ?? processed, "Filters", replaceCurrentIfSameLabel: true, preserveHandles: _cropMode || _selectionMode);

            _status = "Updated";
            RefreshFooter();
            NotifyAll();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ReportError("처리 중 문제가 발생했습니다. 로그를 내보내기에서 확인해 주세요.", ex, "Filter");
        }
    }

    private void ReportError(string userMessage, Exception? ex = null, string category = "Editor")
    {
        _status = userMessage;
        RefreshFooter();
        NotifyAll();

        if (ex is not null)
            _log.Log(Microsoft.Extensions.Logging.LogLevel.Error, category, userMessage, ex);
        else
            _log.Log(Microsoft.Extensions.Logging.LogLevel.Error, category, userMessage);
    }

    private async Task CommitHistoryAsync(byte[] bytes, string label, bool replaceCurrentIfSameLabel = false, bool preserveHandles = false)
    {
        if (!HasImage)
            return;

        var layer = GetActiveLayerInternal();
        if (layer is null)
            return;

        // Trim future history entries on the active layer
        if (layer.HistoryIndex >= 0 && layer.HistoryIndex < layer.History.Count - 1)
            layer.History.RemoveRange(layer.HistoryIndex + 1, layer.History.Count - layer.HistoryIndex - 1);

        var (thumb, size) = await RunImageCpuAsync(
            () => (_imageProcessor.CreateThumbnailDataUrl(bytes), _imageProcessor.GetSize(bytes)),
            inProgressStatus: null);

        if (replaceCurrentIfSameLabel && layer.HistoryIndex >= 0 && layer.HistoryIndex == layer.History.Count - 1 && layer.History[layer.HistoryIndex].Label == label)
        {
            layer.History[layer.HistoryIndex] = new LayerHistoryEntry(bytes.ToArray(), label, DateTime.UtcNow, thumb);
        }
        else
        {
            layer.History.Add(new LayerHistoryEntry(bytes.ToArray(), label, DateTime.UtcNow, thumb));
            layer.HistoryIndex = layer.History.Count - 1;

            if (layer.History.Count > MaxLayerHistoryEntries)
            {
                var remove = layer.History.Count - MaxLayerHistoryEntries;
                layer.History.RemoveRange(0, remove);
                layer.HistoryIndex = Math.Max(0, layer.HistoryIndex - remove);
            }
        }

        // Update layer bytes
        layer.Bytes = bytes.ToArray();

        _viewBytes = GetCompositedBytesOrFallback() ?? bytes;
        (_imageWidth, _imageHeight) = size;

        if (!preserveHandles)
            _handles = new();
    }

    public Task UndoAsync()
    {
        var idx = GetActiveLayerHistoryIndex();
        return GoToHistoryAsync(idx - 1);
    }

    public Task RedoAsync()
    {
        var idx = GetActiveLayerHistoryIndex();
        return GoToHistoryAsync(idx + 1);
    }

    public Task ResetToOriginalAsync() => GoToHistoryAsync(0);

    public async Task GoToHistoryAsync(int index)
    {
        if (!HasImage)
            return;

        var layer = GetActiveLayerInternal();
        if (layer is null)
            return;

        if (index < 0 || index >= layer.History.Count)
            return;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        _perspectiveMode = false;
        layer.HistoryIndex = index;

        var entry = layer.History[index];
        var bytes = entry.Bytes;

        // Update layer bytes from history
        layer.Bytes = bytes.ToArray();

        _viewBytes = GetCompositedBytesOrFallback() ?? bytes;
        (_imageWidth, _imageHeight) = await RunImageCpuAsync(() => _imageProcessor.GetSize(bytes), inProgressStatus: "Loading...");
        _handles = new();

        _status = $"Layer history restored ({layer.Name})";
        RefreshFooter();
        NotifyAll();
        return;
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

        _magicWandDebounceCts?.Cancel();
        _magicWandDebounceCts?.Dispose();
        _magicWandDebounceCts = null;

        _processingLock.Dispose();
    }

    // Layer with per-layer history
    private sealed class LayerEntry
    {
        public Guid Id { get; }
        public string Name { get; set; }
        public byte[] Bytes { get; set; }
        public bool Visible { get; set; }
        public List<LayerHistoryEntry> History { get; } = new();
        public int HistoryIndex { get; set; } = -1;

        public LayerEntry(Guid id, string name, byte[] bytes, bool visible)
        {
            Id = id;
            Name = name;
            Bytes = bytes;
            Visible = visible;
        }

        public LayerEntry Clone()
        {
            var copy = new LayerEntry(Id, Name, Bytes.ToArray(), Visible);
            foreach (var h in History)
                copy.History.Add(h);
            copy.HistoryIndex = HistoryIndex;
            return copy;
        }
    }

    private readonly List<LayerEntry> _layers = new();
    private int _activeLayerIndex = -1;

    private LayerEntry? GetActiveLayerInternal()
        => _activeLayerIndex >= 0 && _activeLayerIndex < _layers.Count ? _layers[_activeLayerIndex] : null;

    private int GetActiveLayerHistoryIndex()
    {
        var layer = GetActiveLayerInternal();
        return layer?.HistoryIndex ?? -1;
    }

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
    {
        get
        {
            if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count)
                return null;

            var layer = _layers[_activeLayerIndex];
            // Return bytes from current history state
            if (layer.History.Count > 0 && layer.HistoryIndex >= 0 && layer.HistoryIndex < layer.History.Count)
                return layer.History[layer.HistoryIndex].Bytes;

            return layer.Bytes;
        }
    }

    private void LayersReset()
    {
        _layers.Clear();
        _activeLayerIndex = -1;
    }

    private void LayersInitFromDocumentBytes(byte[] bytes)
    {
        _layers.Clear();
        var layer = new LayerEntry(Guid.NewGuid(), "Base", bytes.ToArray(), true);
        // Initialize first history entry for the layer
        layer.History.Add(new LayerHistoryEntry(bytes.ToArray(), "Initial", DateTime.UtcNow, null));
        layer.HistoryIndex = 0;
        _layers.Add(layer);
        _activeLayerIndex = 0;
        UpdateViewFromLayers();
    }

    private void SetActiveLayerBytes(byte[] bytes)
    {
        if (_activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count)
            return;

        _layers[_activeLayerIndex].Bytes = bytes;
    }

    /// <summary>
    /// Gets the bytes for a layer based on its current history state.
    /// If history exists, returns bytes from current history index; otherwise returns layer's current bytes.
    /// </summary>
    private byte[] GetLayerBytesFromHistory(LayerEntry layer)
    {
        if (layer.History.Count > 0 && layer.HistoryIndex >= 0 && layer.HistoryIndex < layer.History.Count)
        {
            return layer.History[layer.HistoryIndex].Bytes;
        }
        // Fallback to layer's current bytes if no history
        return layer.Bytes;
    }

    private byte[]? GetCompositedBytesOrFallback()
    {
        if (_layers.Count == 0)
            return CurrentBytes;

        if (_layers.Count == 1)
        {
            if (!_layers[0].Visible)
                return CurrentBytes;
            return GetLayerBytesFromHistory(_layers[0]);
        }

        var visibleBytes = _layers
            .Where(l => l.Visible)
            .Select(GetLayerBytesFromHistory)
            .ToArray();

        if (visibleBytes.Length == 0)
            return CurrentBytes;

        return _imageProcessor.CompositeRgbaLayers(visibleBytes);
    }

    private void UpdateViewFromLayers()
    {
        // Show all visible layers composited together.
        // Users can control which layers are visible via the visibility toggle in the UI.
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
        var newLayer = new LayerEntry(Guid.NewGuid(), $"Layer {_layers.Count + 1}", created.Bytes, true);
        // Initialize first history entry for the new layer
        newLayer.History.Add(new LayerHistoryEntry(created.Bytes.ToArray(), "Initial", DateTime.UtcNow, null));
        newLayer.HistoryIndex = 0;
        _layers.Add(newLayer);
        _activeLayerIndex = _layers.Count - 1;

        UpdateViewFromLayers();
        NotifyAll();
    }

    public Task DuplicateLayerAsync()
    {
        if (!HasImage || _activeLayerIndex < 0 || _activeLayerIndex >= _layers.Count)
            return Task.CompletedTask;

        var src = _layers[_activeLayerIndex];
        var newLayer = new LayerEntry(Guid.NewGuid(), $"{src.Name} Copy", src.Bytes.ToArray(), src.Visible);
        // Initialize first history entry for the duplicated layer
        newLayer.History.Add(new LayerHistoryEntry(src.Bytes.ToArray(), "Duplicated", DateTime.UtcNow, null));
        newLayer.HistoryIndex = 0;
        _layers.Add(newLayer);
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
        lower.Bytes = merged;
        // Add merge to lower layer's history
        lower.History.Add(new LayerHistoryEntry(merged.ToArray(), "Merged", DateTime.UtcNow, null));
        lower.HistoryIndex = lower.History.Count - 1;

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
        layer.Visible = !layer.Visible;

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

        // Immediately update viewBytes with the new result before history is committed.
        // This ensures the canvas shows the latest edit right away.
        // Composite all visible layers including the just-edited one.
        _viewBytes = GetCompositedBytesWithOverride(_activeLayerIndex, next) ?? next;
    }

    /// <summary>
    /// Computes composited bytes of visible layers, using an override for a specific layer index.
    /// Used to show immediate edits before they are committed to history.
    /// </summary>
    private byte[]? GetCompositedBytesWithOverride(int overrideIndex, byte[] overrideBytes)
    {
        if (_layers.Count == 0)
            return CurrentBytes;

        if (_layers.Count == 1)
        {
            if (!_layers[0].Visible)
                return CurrentBytes;
            return overrideIndex == 0 ? overrideBytes : GetLayerBytesFromHistory(_layers[0]);
        }

        var visibleBytes = new List<byte[]>();
        for (var i = 0; i < _layers.Count; i++)
        {
            if (!_layers[i].Visible)
                continue;

            visibleBytes.Add(i == overrideIndex ? overrideBytes : GetLayerBytesFromHistory(_layers[i]));
        }

        if (visibleBytes.Count == 0)
            return CurrentBytes;

        return _imageProcessor.CompositeRgbaLayers(visibleBytes.ToArray());
    }
}
