namespace SharedUI.Components;

public enum CanvasInteractionMode
{
    PanZoom,
    Brush,
    Eraser,
    MagicWand,
    Text
}

public sealed record CanvasStroke(CanvasInteractionMode Mode, IReadOnlyList<CanvasPoint> Points);
