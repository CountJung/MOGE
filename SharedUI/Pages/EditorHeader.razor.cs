using Microsoft.AspNetCore.Components;

namespace SharedUI.Pages;

public partial class EditorHeader
{
    [Parameter] public bool HasImage { get; set; }
    [Parameter] public string? FileName { get; set; }
    [Parameter] public long FileSizeBytes { get; set; }

    [Parameter] public EventCallback New { get; set; }
    [Parameter] public EventCallback OpenImage { get; set; }
    [Parameter] public EventCallback SavePng { get; set; }

    [Parameter] public IReadOnlyList<string>? LoadedImages { get; set; }
    [Parameter] public int SelectedLoadedIndex { get; set; }
    [Parameter] public EventCallback<int> SelectLoadedIndex { get; set; }

    [Parameter] public EventCallback RemoveLoaded { get; set; }
}
