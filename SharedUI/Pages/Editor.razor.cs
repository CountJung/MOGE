using Microsoft.AspNetCore.Components;
using MudBlazor;
using SharedUI.Components.Dialogs;
using SharedUI.Mvvm;
using SharedUI.Logging;
using SharedUI.Services;
using SharedUI.ViewModels;

namespace SharedUI.Pages;

public partial class Editor : ViewModelComponentBase<EditorViewModel>
{
    [CascadingParameter] public SharedUI.Layout.MainLayout? Layout { get; set; }

    [Inject] private MogeLogService LogService { get; set; } = default!;

    private async Task<(string Text, int FontSize, int Thickness, string ColorHex, int Alpha)?> RequestTextInputAsync(
        string initialText, int fontSize, int thickness, string colorHex, int alpha)
    {
        if (DialogService is null)
            return null;

        var parameters = new DialogParameters
        {
            [nameof(TextInputDialog.InitialText)] = initialText,
            [nameof(TextInputDialog.InitialFontSize)] = fontSize,
            [nameof(TextInputDialog.InitialThickness)] = thickness,
            [nameof(TextInputDialog.InitialColorHex)] = colorHex,
            [nameof(TextInputDialog.InitialAlpha)] = alpha,
        };

        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<TextInputDialog>("Text", parameters, options);
        if (dialog is null)
            return null;

        var result = await dialog.Result;
        if (result is null || result.Canceled)
            return null;

        if (result.Data is not TextInputDialog.Result data)
            return null;

        return (data.Text, data.FontSize, data.Thickness, data.ColorHex, data.Alpha);
    }

    private async Task OnNewClickedAsync()
    {
        if (Vm is null)
            return;

        if (DialogService is null)
            return;

        var parameters = new DialogParameters
        {
            [nameof(NewImageDialog.InitialWidth)] = Vm.ImageWidth > 0 ? Vm.ImageWidth : 1024,
            [nameof(NewImageDialog.InitialHeight)] = Vm.ImageHeight > 0 ? Vm.ImageHeight : 768,
        };

        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<NewImageDialog>("New", parameters, options);
        if (dialog is null)
            return;

        var result = await dialog.Result;
        if (result is null || result.Canceled)
            return;

        if (result.Data is not NewImageDialog.Result data)
            return;

        await Vm.CreateNewCanvasAsync(data.Width, data.Height);
    }

    private async Task OnSaveClickedAsync()
    {
        if (Vm is null || !Vm.HasImage)
            return;

        if (DialogService is null)
            return;

        var baseName = FileNameUtil.GetSafeBaseName(Vm.FileName, "image");
        var initialName = baseName;

        var parameters = new DialogParameters
        {
            [nameof(SaveImageDialog.InitialFileName)] = initialName,
            [nameof(SaveImageDialog.InitialFormat)] = ImageExportFormat.Png,
        };

        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.ExtraSmall,
            FullWidth = true
        };

        var dialog = await DialogService.ShowAsync<SaveImageDialog>("Save", parameters, options);
        if (dialog is null)
            return;

        var result = await dialog.Result;
        if (result is null || result.Canceled)
            return;

        if (result.Data is not SaveImageDialog.Result data)
            return;

        await Vm.SaveAsAsync(data.FileName, data.Format);
    }

    protected override EditorViewModel CreateViewModel()
        => new(ImageFilePicker, Document, ImageProcessor, ImageExport, LogService);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Vm!.Initialize();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);

        Vm!.SetFooterPusher(msg => Layout?.PushFooterMessage(msg));
        Vm!.SetTextInputRequester(RequestTextInputAsync);

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
