using Microsoft.AspNetCore.Components;
using SharedUI.Components;
using Microsoft.AspNetCore.Components.Web;

namespace SharedUI.Pages;

public partial class EditorToolBar
{
    [Parameter] public bool HasImage { get; set; }
    [Parameter] public bool PerspectiveMode { get; set; }
    [Parameter] public bool CropMode { get; set; }

    [Parameter] public CanvasInteractionMode InteractionMode { get; set; }
    [Parameter] public EventCallback<CanvasInteractionMode> InteractionModeChanged { get; set; }

    [Parameter] public int BrushRadius { get; set; }
    [Parameter] public EventCallback<int> BrushRadiusChanged { get; set; }

    [Parameter] public bool SelectionMode { get; set; }
    [Parameter] public EventCallback<bool> SelectionModeChanged { get; set; }

    [Parameter] public int SelectionBlurKernelSize { get; set; }
    [Parameter] public EventCallback<int> SelectionBlurKernelSizeChanged { get; set; }

    [Parameter] public double SharpenAmount { get; set; }
    [Parameter] public EventCallback<double> SharpenAmountChanged { get; set; }

    [Parameter] public EventCallback ApplySelectionBlur { get; set; }
    [Parameter] public EventCallback ApplySelectionSharpen { get; set; }

    [Parameter] public string ForegroundColorHex { get; set; } = "#000000";
    [Parameter] public EventCallback<string> ForegroundColorHexChanged { get; set; }

    [Parameter] public int ForegroundAlpha { get; set; } = 255;
    [Parameter] public EventCallback<int> ForegroundAlphaChanged { get; set; }

    [Parameter] public string BackgroundColorHex { get; set; } = "#ffffff";
    [Parameter] public EventCallback<string> BackgroundColorHexChanged { get; set; }

    [Parameter] public int BackgroundAlpha { get; set; } = 255;
    [Parameter] public EventCallback<int> BackgroundAlphaChanged { get; set; }

    [Parameter] public EventCallback FillSelection { get; set; }

    [Parameter] public bool CanFillSelection { get; set; }

    [Parameter] public int MagicWandTolerance { get; set; }
    [Parameter] public EventCallback<int> MagicWandToleranceChanged { get; set; }

    private bool ToolDisabled => !HasImage || PerspectiveMode || CropMode;

    private bool IsSelection => SelectionMode;
    private bool IsPanZoom => !SelectionMode && InteractionMode == CanvasInteractionMode.PanZoom;
    private bool IsBrush => !SelectionMode && InteractionMode == CanvasInteractionMode.Brush;
    private bool IsEraser => !SelectionMode && InteractionMode == CanvasInteractionMode.Eraser;
    private bool IsMagicWand => !SelectionMode && InteractionMode == CanvasInteractionMode.MagicWand;
    private bool IsTextTool => !SelectionMode && InteractionMode == CanvasInteractionMode.Text;

    private async Task SelectPanZoom()
    {
        if (ToolDisabled)
            return;

        if (SelectionMode)
            await SelectionModeChanged.InvokeAsync(false);

        await InteractionModeChanged.InvokeAsync(CanvasInteractionMode.PanZoom);
    }

    private async Task SelectBrush()
    {
        if (ToolDisabled)
            return;

        if (SelectionMode)
            await SelectionModeChanged.InvokeAsync(false);

        await InteractionModeChanged.InvokeAsync(CanvasInteractionMode.Brush);
    }

    private async Task SelectMagicWand()
    {
        if (ToolDisabled)
            return;

        if (SelectionMode)
            await SelectionModeChanged.InvokeAsync(false);

        await InteractionModeChanged.InvokeAsync(CanvasInteractionMode.MagicWand);
    }

    private async Task SelectTextTool()
    {
        if (ToolDisabled)
            return;

        if (SelectionMode)
            await SelectionModeChanged.InvokeAsync(false);

        await InteractionModeChanged.InvokeAsync(CanvasInteractionMode.Text);
    }

    private async Task SelectEraser()
    {
        if (ToolDisabled)
            return;

        if (SelectionMode)
            await SelectionModeChanged.InvokeAsync(false);

        await InteractionModeChanged.InvokeAsync(CanvasInteractionMode.Eraser);
    }

    private async Task SelectSelection()
    {
        if (ToolDisabled)
            return;

        await SelectionModeChanged.InvokeAsync(true);
    }

    private Task OnForegroundColorInput(ChangeEventArgs e)
    {
        var v = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(v))
            return Task.CompletedTask;

        return ForegroundColorHexChanged.InvokeAsync(v);
    }

    private Task OnForegroundAlphaChanged(int a)
        => ForegroundAlphaChanged.InvokeAsync(Math.Clamp(a, 0, 255));

    private Task SetForegroundTransparent()
        => ForegroundAlphaChanged.InvokeAsync(0);

    private Task OnBackgroundColorInput(ChangeEventArgs e)
    {
        var v = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(v))
            return Task.CompletedTask;

        return BackgroundColorHexChanged.InvokeAsync(v);
    }

    private Task OnBackgroundAlphaChanged(int a)
        => BackgroundAlphaChanged.InvokeAsync(Math.Clamp(a, 0, 255));

    private Task SetBackgroundTransparent()
        => BackgroundAlphaChanged.InvokeAsync(0);
}
