using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Filekin.App.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for the view models. Hand-rolled rather than
/// pulling in an MVVM framework, because the shell's binding needs are small
/// (ENGINEERING-GUARDRAILS.md — avoid unnecessary dependencies).
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
