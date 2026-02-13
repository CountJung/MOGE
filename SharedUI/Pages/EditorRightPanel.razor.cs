using Microsoft.AspNetCore.Components;
using SharedUI.Services;
using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class EditorRightPanel
{
    private readonly EditorRightPanelViewModel _vm = new();

    [Parameter] public bool HasImage { get; set; }

    [Parameter] public bool PerspectiveMode { get; set; }
    [Parameter] public EventCallback<bool> PerspectiveModeChanged { get; set; }

    [Parameter] public bool CropMode { get; set; }
    [Parameter] public EventCallback<bool> CropModeChanged { get; set; }

    [Parameter] public EventCallback ApplyCrop { get; set; }

    [Parameter] public EventCallback ApplyPerspective { get; set; }

    [Parameter] public EventCallback RotateLeft { get; set; }
    [Parameter] public EventCallback RotateRight { get; set; }
    [Parameter] public EventCallback ResizeHalf { get; set; }

    [Parameter] public int BlurKernelSize { get; set; }
    [Parameter] public EventCallback<int> BlurKernelSizeChanged { get; set; }

    [Parameter] public bool Grayscale { get; set; }
    [Parameter] public EventCallback<bool> GrayscaleChanged { get; set; }

    [Parameter] public bool Sepia { get; set; }
    [Parameter] public EventCallback<bool> SepiaChanged { get; set; }

    [Parameter] public bool Invert { get; set; }
    [Parameter] public EventCallback<bool> InvertChanged { get; set; }

    [Parameter] public double Saturation { get; set; }
    [Parameter] public EventCallback<double> SaturationChanged { get; set; }

    [Parameter] public bool Sketch { get; set; }
    [Parameter] public EventCallback<bool> SketchChanged { get; set; }

    [Parameter] public bool Cartoon { get; set; }
    [Parameter] public EventCallback<bool> CartoonChanged { get; set; }

    [Parameter] public bool Emboss { get; set; }
    [Parameter] public EventCallback<bool> EmbossChanged { get; set; }

    [Parameter] public double FilterSharpenAmount { get; set; }
    [Parameter] public EventCallback<double> FilterSharpenAmountChanged { get; set; }

    [Parameter] public double GlowStrength { get; set; }
    [Parameter] public EventCallback<double> GlowStrengthChanged { get; set; }

    [Parameter] public ColorMapStyle ColorMap { get; set; }
    [Parameter] public EventCallback<ColorMapStyle> ColorMapChanged { get; set; }

    [Parameter] public double ColorMapStrength { get; set; }
    [Parameter] public EventCallback<double> ColorMapStrengthChanged { get; set; }

    [Parameter] public int PosterizeLevels { get; set; }
    [Parameter] public EventCallback<int> PosterizeLevelsChanged { get; set; }

    [Parameter] public int PixelizeBlockSize { get; set; }
    [Parameter] public EventCallback<int> PixelizeBlockSizeChanged { get; set; }

    [Parameter] public double VignetteStrength { get; set; }
    [Parameter] public EventCallback<double> VignetteStrengthChanged { get; set; }

    [Parameter] public double NoiseAmount { get; set; }
    [Parameter] public EventCallback<double> NoiseAmountChanged { get; set; }

    [Parameter] public bool Canny { get; set; }
    [Parameter] public EventCallback<bool> CannyChanged { get; set; }

    [Parameter] public double CannyT1 { get; set; }
    [Parameter] public EventCallback<double> CannyT1Changed { get; set; }

    [Parameter] public double CannyT2 { get; set; }
    [Parameter] public EventCallback<double> CannyT2Changed { get; set; }

    [Parameter] public double Contrast { get; set; }
    [Parameter] public EventCallback<double> ContrastChanged { get; set; }

    [Parameter] public double Brightness { get; set; }
    [Parameter] public EventCallback<double> BrightnessChanged { get; set; }

    [Parameter] public bool CanUndo { get; set; }
    [Parameter] public bool CanRedo { get; set; }

    [Parameter] public int CurrentHistoryIndex { get; set; } = -1;

    [Parameter] public EventCallback Undo { get; set; }
    [Parameter] public EventCallback Redo { get; set; }
    [Parameter] public EventCallback Reset { get; set; }

    [Parameter] public IReadOnlyList<EditorHistoryListItem>? HistoryItems { get; set; }
    [Parameter] public EventCallback<int> HistoryItemSelected { get; set; }

    [Parameter] public IReadOnlyList<(Guid Id, string Name, bool Visible, int Index)>? Layers { get; set; }
    [Parameter] public int ActiveLayerIndex { get; set; } = -1;

    [Parameter] public EventCallback<int> LayerSelected { get; set; }

    [Parameter] public EventCallback AddLayer { get; set; }
    [Parameter] public EventCallback DuplicateLayer { get; set; }
    [Parameter] public EventCallback DeleteLayer { get; set; }
    [Parameter] public EventCallback MergeDownLayer { get; set; }

    [Parameter] public bool CanDeleteLayer { get; set; }
    [Parameter] public bool CanMergeDown { get; set; }
    [Parameter] public bool CanToggleLayerVisibility { get; set; }
    [Parameter] public EventCallback<int> ToggleLayerVisibility { get; set; }

    private string ToggleIconStyle(bool isActive) => _vm.ToggleIconStyle(isActive);

    private string ActionIconStyle(bool isEnabled) => _vm.ActionIconStyle(isEnabled);

    private string HistoryItemStyle(bool isCurrent) => _vm.HistoryItemStyle(isCurrent);

    private Task OnHistoryItemClicked(int index)
        => _vm.CanSelectHistoryItem(HasImage, index, CurrentHistoryIndex)
            ? HistoryItemSelected.InvokeAsync(index)
            : Task.CompletedTask;

    private Task TogglePerspectiveMode() => PerspectiveModeChanged.InvokeAsync(!PerspectiveMode);

    private Task ToggleCropMode()
        => _vm.CanToggleCropMode(PerspectiveMode)
            ? CropModeChanged.InvokeAsync(!CropMode)
            : Task.CompletedTask;

    private Task ToggleGrayscale()
        => _vm.CanToggleGrayscale(Sepia)
            ? GrayscaleChanged.InvokeAsync(!Grayscale)
            : Task.CompletedTask;

    private Task ToggleSepia() => SepiaChanged.InvokeAsync(!Sepia);

    private Task ToggleInvert() => InvertChanged.InvokeAsync(!Invert);

    private Task ToggleSketch() => SketchChanged.InvokeAsync(!Sketch);

    private Task ToggleCartoon() => CartoonChanged.InvokeAsync(!Cartoon);

    private Task ToggleEmboss() => EmbossChanged.InvokeAsync(!Emboss);

    private Task ToggleCanny() => CannyChanged.InvokeAsync(!Canny);

    private Task SelectLayerAsync(int index)
        => _vm.CanSelectLayer(HasImage, index, ActiveLayerIndex)
            ? LayerSelected.InvokeAsync(index)
            : Task.CompletedTask;

    private string LayerItemStyle(bool isActive) => _vm.LayerItemStyle(isActive);
}
