using Microsoft.AspNetCore.Components;
using SharedUI.Logging;
using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class Settings
{
    [Inject] private ILogExportService LogExport { get; set; } = default!;
    [Inject] private MogeLogService LogService { get; set; } = default!;

    protected override SettingsViewModel CreateViewModel()
        => new(SettingsService, Nav, Js, LogExport, LogService);

    protected override async Task OnInitializedAsync()
    {
        await Vm!.InitializeAsync();
    }
}
