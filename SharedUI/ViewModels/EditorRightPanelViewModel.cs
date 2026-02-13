namespace SharedUI.ViewModels;

public sealed class EditorRightPanelViewModel
{
    public string ToggleIconStyle(bool isActive)
    {
        var borderColor = isActive ? "var(--mud-palette-primary)" : "var(--mud-palette-lines-default)";
        return $"border:1px solid {borderColor}; border-radius: var(--mud-default-borderradius);";
    }

    public string ActionIconStyle(bool isEnabled)
    {
        var borderColor = isEnabled ? "var(--mud-palette-primary)" : "var(--mud-palette-lines-default)";
        return $"border:1px solid {borderColor}; border-radius: var(--mud-default-borderradius);";
    }

    public string HistoryItemStyle(bool isCurrent)
        => isCurrent
            ? "border-left:3px solid var(--mud-palette-primary);"
            : "border-left:3px solid transparent;";

    public bool CanSelectHistoryItem(bool hasImage, int index, int currentHistoryIndex)
        => hasImage && index != currentHistoryIndex;

    public bool CanToggleCropMode(bool perspectiveMode) => !perspectiveMode;

    public bool CanToggleGrayscale(bool sepia) => !sepia;

    public bool CanSelectLayer(bool hasImage, int index, int activeLayerIndex)
        => hasImage && index != activeLayerIndex;

    public string LayerItemStyle(bool isActive)
    {
        var border = isActive
            ? "2px solid var(--mud-palette-primary)"
            : "1px solid var(--mud-palette-lines-default)";

        return $"border:{border}; border-radius: var(--mud-default-borderradius); cursor:pointer; user-select:none;";
    }
}
