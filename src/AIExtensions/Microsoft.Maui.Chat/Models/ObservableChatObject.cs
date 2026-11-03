using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Chat;

/// <summary>Base class for renderer-neutral chat models with observable properties.</summary>
public abstract class ObservableChatObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets a property and raises <see cref="PropertyChanged"/> when it changed.</summary>
    /// <typeparam name="T">The property type.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The candidate value.</param>
    /// <param name="propertyName">The changed property name.</param>
    /// <returns><see langword="true"/> when the property changed.</returns>
    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    /// <param name="propertyName">The changed property name.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
