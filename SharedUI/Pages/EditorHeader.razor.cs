using Microsoft.AspNetCore.Components;

namespace SharedUI.Pages;

public partial class EditorHeader
{
    [Parameter] public bool HasImage { get; set; }
    [Parameter] public string? FileName { get; set; }
    [Parameter] public long FileSizeBytes { get; set; }

    [Parameter] public EventCallback OpenImage { get; set; }
    [Parameter] public EventCallback SavePng { get; set; }
    [Parameter] public EventCallback Clear { get; set; }
}
