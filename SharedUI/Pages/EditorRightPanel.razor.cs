using Microsoft.AspNetCore.Components;
using SharedUI.Services;
using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class EditorRightPanel
{
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

    private static string ToggleIconStyle(bool isActive)
    {
        var borderColor = isActive ? "var(--mud-palette-primary)" : "var(--mud-palette-lines-default)";
        return $"border:1px solid {borderColor}; border-radius: var(--mud-default-borderradius);";
    }

    private static string ActionIconStyle(bool isEnabled)
    {
        var borderColor = isEnabled ? "var(--mud-palette-primary)" : "var(--mud-palette-lines-default)";
        return $"border:1px solid {borderColor}; border-radius: var(--mud-default-borderradius);";
    }

    private static string HistoryItemStyle(bool isCurrent)
    {
        if (!isCurrent)
            return "border-left:3px solid transparent;";

        return "border-left:3px solid var(--mud-palette-primary);";
    }

    private Task OnHistoryItemClicked(int index)
    {
        if (!HasImage)
            return Task.CompletedTask;

        if (index == CurrentHistoryIndex)
            return Task.CompletedTask;

        return HistoryItemSelected.InvokeAsync(index);
    }

    private Task TogglePerspectiveMode() => PerspectiveModeChanged.InvokeAsync(!PerspectiveMode);

    private Task ToggleCropMode()
    {
        if (PerspectiveMode)
            return Task.CompletedTask;

        return CropModeChanged.InvokeAsync(!CropMode);
    }

    private Task ToggleGrayscale()
    {
        if (Sepia)
            return Task.CompletedTask;

        return GrayscaleChanged.InvokeAsync(!Grayscale);
    }

    private Task ToggleSepia() => SepiaChanged.InvokeAsync(!Sepia);

    private Task ToggleInvert() => InvertChanged.InvokeAsync(!Invert);

    private Task ToggleSketch() => SketchChanged.InvokeAsync(!Sketch);

    private Task ToggleCartoon() => CartoonChanged.InvokeAsync(!Cartoon);

    private Task ToggleEmboss() => EmbossChanged.InvokeAsync(!Emboss);

    private Task ToggleCanny() => CannyChanged.InvokeAsync(!Canny);
}
