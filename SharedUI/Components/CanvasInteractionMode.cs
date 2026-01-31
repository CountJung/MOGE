namespace SharedUI.Components;

public enum CanvasInteractionMode
{
    PanZoom,
    Brush,
    Eraser,
    MagicWand,
    Text,
    LassoSelection
}

public sealed record CanvasStroke(CanvasInteractionMode Mode, IReadOnlyList<CanvasPoint> Points);
