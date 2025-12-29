using Microsoft.AspNetCore.Components;
using SharedUI.Components;

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

    private bool ToolDisabled => !HasImage || PerspectiveMode || CropMode;

    private bool IsSelection => SelectionMode;
    private bool IsPanZoom => !SelectionMode && InteractionMode == CanvasInteractionMode.PanZoom;
    private bool IsBrush => !SelectionMode && InteractionMode == CanvasInteractionMode.Brush;
    private bool IsEraser => !SelectionMode && InteractionMode == CanvasInteractionMode.Eraser;

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
}
