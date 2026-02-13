using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SharedUI.Components;
using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class EditorToolBar
{
    private readonly EditorToolBarViewModel _vm = new();

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

    [Parameter] public bool CanCopySelection { get; set; }
    [Parameter] public bool CanPaste { get; set; }

    [Parameter] public EventCallback CopySelection { get; set; }
    [Parameter] public EventCallback CutSelection { get; set; }
    [Parameter] public EventCallback PasteClipboard { get; set; }

    [Parameter] public int MagicWandTolerance { get; set; }
    [Parameter] public EventCallback<int> MagicWandToleranceChanged { get; set; }

    private bool ToolDisabled => _vm.ToolDisabled(HasImage, PerspectiveMode, CropMode);

    private bool IsSelection => _vm.IsSelection(SelectionMode);
    private bool IsPanZoom => _vm.IsPanZoom(SelectionMode, InteractionMode);
    private bool IsBrush => _vm.IsBrush(SelectionMode, InteractionMode);
    private bool IsEraser => _vm.IsEraser(SelectionMode, InteractionMode);
    private bool IsMagicWand => _vm.IsMagicWand(SelectionMode, InteractionMode);
    private bool IsTextTool => _vm.IsTextTool(SelectionMode, InteractionMode);
    private bool IsLasso => _vm.IsLasso(SelectionMode, InteractionMode);

    private Task SelectPanZoom() => SelectModeAsync(CanvasInteractionMode.PanZoom);
    private Task SelectBrush() => SelectModeAsync(CanvasInteractionMode.Brush);
    private Task SelectMagicWand() => SelectModeAsync(CanvasInteractionMode.MagicWand);
    private Task SelectTextTool() => SelectModeAsync(CanvasInteractionMode.Text);
    private Task SelectEraser() => SelectModeAsync(CanvasInteractionMode.Eraser);
    private Task SelectLasso() => SelectModeAsync(CanvasInteractionMode.LassoSelection);

    private async Task SelectModeAsync(CanvasInteractionMode mode)
    {
        if (ToolDisabled)
            return;

        if (SelectionMode)
            await SelectionModeChanged.InvokeAsync(false);

        await InteractionModeChanged.InvokeAsync(mode);
    }

    private Task SelectSelection()
        => _vm.CanEnableSelection(ToolDisabled)
            ? SelectionModeChanged.InvokeAsync(true)
            : Task.CompletedTask;

    private Task OnForegroundColorInput(ChangeEventArgs e)
    {
        var value = _vm.ReadColorHex(e);
        return value is null
            ? Task.CompletedTask
            : ForegroundColorHexChanged.InvokeAsync(value);
    }

    private Task OnForegroundAlphaChanged(int alpha)
        => ForegroundAlphaChanged.InvokeAsync(_vm.ClampAlpha(alpha));

    private Task SetForegroundTransparent()
        => ForegroundAlphaChanged.InvokeAsync(0);

    private Task OnBackgroundColorInput(ChangeEventArgs e)
    {
        var value = _vm.ReadColorHex(e);
        return value is null
            ? Task.CompletedTask
            : BackgroundColorHexChanged.InvokeAsync(value);
    }

    private Task OnBackgroundAlphaChanged(int alpha)
        => BackgroundAlphaChanged.InvokeAsync(_vm.ClampAlpha(alpha));

    private Task SetBackgroundTransparent()
        => BackgroundAlphaChanged.InvokeAsync(0);
}
