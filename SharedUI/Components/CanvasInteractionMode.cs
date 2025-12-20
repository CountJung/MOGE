namespace SharedUI.Components;

public enum CanvasInteractionMode
{
    PanZoom,
    Brush,
    Eraser
}

public sealed record CanvasStroke(CanvasInteractionMode Mode, IReadOnlyList<CanvasPoint> Points);
