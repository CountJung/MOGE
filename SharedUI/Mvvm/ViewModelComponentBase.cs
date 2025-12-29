using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace SharedUI.Mvvm;

public abstract class ViewModelComponentBase<TViewModel> : ComponentBase, IDisposable
    where TViewModel : class, INotifyPropertyChanged
{
    protected TViewModel? Vm { get; private set; }

    protected abstract TViewModel CreateViewModel();

    protected override void OnInitialized()
    {
        Vm = CreateViewModel();
        Vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        if (Vm is not null)
            Vm.PropertyChanged -= OnVmPropertyChanged;

        if (Vm is IDisposable d)
            d.Dispose();
    }
}
