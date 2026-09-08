using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// C# parity for Kotlin's
    /// <c>produceState(initialValue, vararg keys) { producer }</c>:
    /// remembers a <see cref="MutableState{T}"/> seeded with
    /// <paramref name="initialValue"/>. Starts <paramref name="producer"/>
    /// the first time this call site enters the composition. The
    /// producer receives the state to write to plus a
    /// <see cref="CancellationToken"/> that fires when this call site
    /// leaves the composition.
    /// </summary>
    public static MutableState<T> ProduceState<T>(
        this IComposer composer,
        T initialValue,
        Func<MutableState<T>, CancellationToken, Task> producer,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => ProduceStateCore(composer, initialValue, producer, keys: null, line, file);

    /// <summary>
    /// Keyed <c>produceState(initial, key1) { producer }</c>: cancels
    /// the running producer and starts a fresh one whenever
    /// <paramref name="key1"/> changes (structural equality).
    /// </summary>
    public static MutableState<T> ProduceState<T>(
        this IComposer composer,
        T initialValue,
        object? key1,
        Func<MutableState<T>, CancellationToken, Task> producer,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => ProduceStateCore(composer, initialValue, producer, new[] { key1 }, line, file);

    /// <summary>Keyed <c>produceState(initial, key1, key2) { producer }</c>.</summary>
    public static MutableState<T> ProduceState<T>(
        this IComposer composer,
        T initialValue,
        object? key1,
        object? key2,
        Func<MutableState<T>, CancellationToken, Task> producer,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => ProduceStateCore(composer, initialValue, producer, new[] { key1, key2 }, line, file);

    /// <summary>Keyed <c>produceState(initial, key1, key2, key3) { producer }</c>.</summary>
    public static MutableState<T> ProduceState<T>(
        this IComposer composer,
        T initialValue,
        object? key1,
        object? key2,
        object? key3,
        Func<MutableState<T>, CancellationToken, Task> producer,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => ProduceStateCore(composer, initialValue, producer, new[] { key1, key2, key3 }, line, file);

    /// <summary>
    /// Array-form keyed <c>produceState(initial, vararg keys) { producer }</c>.
    /// </summary>
    public static MutableState<T> ProduceStateKeyed<T>(
        this IComposer composer,
        T initialValue,
        object?[] keys,
        Func<MutableState<T>, CancellationToken, Task> producer,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        => ProduceStateCore(composer, initialValue, producer, keys ?? throw new ArgumentNullException(nameof(keys)), line, file);

    static MutableState<T> ProduceStateCore<T>(
        IComposer composer,
        T initialValue,
        Func<MutableState<T>, CancellationToken, Task> producer,
        object?[]? keys,
        int line,
        string file)
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(producer);

        composer.StartReplaceableGroup(SourceLocationKey.Compute(line, file));
        try
        {
            // The slot value MUST be the IRememberObserver itself —
            // Compose only inspects what UpdateRememberedValue is
            // handed for the IRememberObserver interface. Nesting it
            // inside RememberHolder would silently break the
            // OnRemembered/OnForgotten/OnAbandoned hooks.
            if (composer.RememberedValue() is ProduceStateScope<T> existing)
            {
                if (!RememberHolder.KeysEqual(existing.Keys, keys))
                    existing.Restart(keys);
                return existing.State;
            }
            var scope = new ProduceStateScope<T>(initialValue, producer, keys);
            composer.UpdateRememberedValue(scope);
            return scope.State;
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }
}
