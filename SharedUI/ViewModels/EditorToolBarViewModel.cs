using Microsoft.AspNetCore.Components.Web;
using SharedUI.Components;

namespace SharedUI.ViewModels;

public sealed class EditorToolBarViewModel
{
    public bool ToolDisabled(bool hasImage, bool perspectiveMode, bool cropMode)
        => !hasImage || perspectiveMode || cropMode;

    public bool IsSelection(bool selectionMode) => selectionMode;

    public bool IsPanZoom(bool selectionMode, CanvasInteractionMode interactionMode)
        => !selectionMode && interactionMode == CanvasInteractionMode.PanZoom;

    public bool IsBrush(bool selectionMode, CanvasInteractionMode interactionMode)
        => !selectionMode && interactionMode == CanvasInteractionMode.Brush;

    public bool IsEraser(bool selectionMode, CanvasInteractionMode interactionMode)
        => !selectionMode && interactionMode == CanvasInteractionMode.Eraser;

    public bool IsMagicWand(bool selectionMode, CanvasInteractionMode interactionMode)
        => !selectionMode && interactionMode == CanvasInteractionMode.MagicWand;

    public bool IsTextTool(bool selectionMode, CanvasInteractionMode interactionMode)
        => !selectionMode && interactionMode == CanvasInteractionMode.Text;

    public bool IsLasso(bool selectionMode, CanvasInteractionMode interactionMode)
        => !selectionMode && interactionMode == CanvasInteractionMode.LassoSelection;

    public async Task<CanvasInteractionMode?> SelectModeAsync(bool toolDisabled, bool selectionMode, CanvasInteractionMode mode)
    {
        if (toolDisabled)
            return null;

        if (selectionMode)
            return CanvasInteractionMode.PanZoom;

        return await Task.FromResult(mode);
    }

    public bool CanEnableSelection(bool toolDisabled) => !toolDisabled;

    public string? ReadColorHex(ChangeEventArgs e)
    {
        var value = e.Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public int ClampAlpha(int alpha) => Math.Clamp(alpha, 0, 255);
}
