using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Tree-syntax wrapper around
/// <see cref="ComposeExtensions.DisposableEffect(object?, Func{DisposableEffectScope, Action})"/>.
/// Re-runs <c>Effect</c> on first composition and any time
/// <c>Key1</c> / <c>Key2</c> / <c>Key3</c> changes; calls the cleanup
/// <see cref="Action"/> on key change or when this node leaves
/// the composition.
/// </summary>
/// <remarks>
/// <code>
/// new DisposableEffect(_sensorId, scope =&gt;
/// {
///     var registration = SensorRegistry.Subscribe(_sensorId, OnSensorTick);
///     return () =&gt; registration.Dispose();
/// })
/// </code>
/// </remarks>
public sealed class DisposableEffect : ComposableNode
{
    readonly object? _key1, _key2, _key3;
    readonly int _keyCount;
    readonly Func<DisposableEffectScope, Action> _effect;

    /// <summary>Single-key form.</summary>
    public DisposableEffect(
        object? key1,
        Func<DisposableEffectScope, Action> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _key1 = key1; _keyCount = 1; _effect = effect;
    }

    /// <summary>Two-key form.</summary>
    public DisposableEffect(
        object? key1,
        object? key2,
        Func<DisposableEffectScope, Action> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _key1 = key1; _key2 = key2; _keyCount = 2; _effect = effect;
    }

    /// <summary>Three-key form.</summary>
    public DisposableEffect(
        object? key1,
        object? key2,
        object? key3,
        Func<DisposableEffectScope, Action> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _key1 = key1; _key2 = key2; _key3 = key3; _keyCount = 3; _effect = effect;
    }

    public override void Render(IComposer composer)
    {
        switch (_keyCount)
        {
            case 1: composer.DisposableEffect(_key1, _effect); break;
            case 2: composer.DisposableEffect(_key1, _key2, _effect); break;
            default: composer.DisposableEffect(_key1, _key2, _key3, _effect); break;
        }
    }
}
