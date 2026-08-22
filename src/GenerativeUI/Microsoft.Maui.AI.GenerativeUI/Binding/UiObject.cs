using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.GenerativeUI.Binding;

/// <summary>
/// One node in the generic, observable data tree that stands in for a hand-authored view model.
/// A node is a scalar leaf (<see cref="Value"/>), an object (keyed members via <see cref="this[string]"/>),
/// or a list (<see cref="Children"/>). The inflator assigns a <see cref="UiObject"/> root as the
/// <c>BindingContext</c> and compiles DSL <c>bind</c>/<c>key</c> paths into indexer bindings against it.
/// </summary>
/// <remarks>
/// Indexer + <see cref="INotifyPropertyChanged"/> is the binding substrate (portable and AOT-friendly),
/// deliberately avoiding <c>System.Dynamic</c> which is unreliable under the iOS interpreter / NativeAOT.
/// See <c>docs/GenerativeUI/spec/appendix-binding-model.md</c>.
/// </remarks>
public sealed class UiObject : INotifyPropertyChanged
{
    private readonly Dictionary<string, UiObject> _members = new(StringComparer.Ordinal);
    private object? _value;

    public UiObject(string? name = null) => Name = name;

    /// <summary>The member name (when this node is a keyed child), else <c>null</c> for a root.</summary>
    public string? Name { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The scalar value. Two-way bindable; raises <see cref="PropertyChanged"/> on change.</summary>
    public object? Value
    {
        get => _value;
        set
        {
            if (Equals(_value, value))
                return;
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AsString));
        }
    }

    /// <summary>
    /// Object member access (<c>root["product"]["name"]</c>). Auto-vivifies a stable empty child so
    /// missing bind paths resolve to an empty leaf instead of throwing. The same instance is returned
    /// for a given key so two-way bindings stay attached across reads.
    /// </summary>
    public UiObject this[string key]
    {
        get
        {
            if (!_members.TryGetValue(key, out var child))
            {
                child = new UiObject(key);
                _members[key] = child;
                OnPropertyChanged(Binding.Indexer);
            }
            return child;
        }
    }

    /// <summary>List members, bound as a <c>CollectionView.ItemsSource</c> when this node is an array.</summary>
    public UiObjectCollection Children { get; } = new();

    /// <summary>True when the given member already exists (does not auto-vivify).</summary>
    public bool HasMember(string key) => _members.ContainsKey(key);

    /// <summary>Removes a member if present; raises the indexer change so bindings re-evaluate.</summary>
    public bool RemoveMember(string key)
    {
        if (!_members.Remove(key))
            return false;
        OnPropertyChanged(Binding.Indexer);
        return true;
    }

    /// <summary>Enumerates existing members (does not auto-vivify).</summary>
    public IEnumerable<KeyValuePair<string, UiObject>> Members => _members;

    // Typed convenience accessors used by converters / the inflator.

    public string? AsString() => _value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        double d => d.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => _value.ToString(),
    };

    public double? AsNumber() => _value switch
    {
        null => null,
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) => r,
        _ => null,
    };

    public bool? AsBool() => _value switch
    {
        null => null,
        bool b => b,
        string s when bool.TryParse(s, out var r) => r,
        double d => d != 0,
        _ => null,
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static class Binding
    {
        // The conventional name MAUI raises/observes for indexer changes.
        public const string Indexer = "Item[]";
    }
}
