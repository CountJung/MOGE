using Microsoft.AspNetCore.Components;
using SharedUI.Mvvm;
using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class Editor : ViewModelComponentBase<EditorViewModel>
{
    [CascadingParameter] public SharedUI.Layout.MainLayout? Layout { get; set; }

    protected override EditorViewModel CreateViewModel()
        => new(ImageFilePicker, Document, ImageProcessor, ImageExport);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Vm!.Initialize();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);

        Vm!.SetFooterPusher(msg => Layout?.PushFooterMessage(msg));

        if (Layout is not null && !Vm.LayoutShortcutsSubscribed)
        {
            Layout.UndoRequested += Vm.OnLayoutUndoRequestedAsync;
            Layout.RedoRequested += Vm.OnLayoutRedoRequestedAsync;
            Vm.SetLayoutShortcutsSubscribed(true);
        }
    }

    public override void Dispose()
    {
        if (Layout is not null && Vm is not null && Vm.LayoutShortcutsSubscribed)
        {
            Layout.UndoRequested -= Vm.OnLayoutUndoRequestedAsync;
            Layout.RedoRequested -= Vm.OnLayoutRedoRequestedAsync;
            Vm.SetLayoutShortcutsSubscribed(false);
        }

        base.Dispose();
    }
}
