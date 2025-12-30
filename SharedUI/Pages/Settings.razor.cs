using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class Settings
{
    protected override SettingsViewModel CreateViewModel()
        => new(SettingsService, Nav, Js);

    protected override async Task OnInitializedAsync()
    {
        await Vm!.InitializeAsync();
    }
}
