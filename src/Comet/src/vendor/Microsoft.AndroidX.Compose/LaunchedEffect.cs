using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Tree-syntax wrapper around
/// <see cref="ComposeExtensions.LaunchedEffect(object?, Func{CancellationToken, Task})"/>.
/// Launches <c>Body</c> as a C# <see cref="Task"/>
/// on first composition and any time a key changes; cancels the
/// supplied <see cref="CancellationToken"/> on key
/// change or when this node leaves the composition.
/// </summary>
/// <remarks>
/// <code>
/// new LaunchedEffect(_sessionId, async ct =&gt;
/// {
///     while (!ct.IsCancellationRequested)
///     {
///         await Task.Delay(1000, ct);
///         _ticks.Value++;
///     }
/// })
/// </code>
/// </remarks>
public sealed class LaunchedEffect : ComposableNode
{
    readonly object? _key1, _key2, _key3;
    readonly int _keyCount;
    readonly Func<CancellationToken, Task> _body;

    /// <summary>Single-key form.</summary>
    public LaunchedEffect(
        object? key1,
        Func<CancellationToken, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _key1 = key1; _keyCount = 1; _body = body;
    }

    /// <summary>Two-key form.</summary>
    public LaunchedEffect(
        object? key1,
        object? key2,
        Func<CancellationToken, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _key1 = key1; _key2 = key2; _keyCount = 2; _body = body;
    }

    /// <summary>Three-key form.</summary>
    public LaunchedEffect(
        object? key1,
        object? key2,
        object? key3,
        Func<CancellationToken, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _key1 = key1; _key2 = key2; _key3 = key3; _keyCount = 3; _body = body;
    }

    public override void Render(IComposer composer)
    {
        switch (_keyCount)
        {
            case 1: composer.LaunchedEffect(_key1, _body); break;
            case 2: composer.LaunchedEffect(_key1, _key2, _body); break;
            default: composer.LaunchedEffect(_key1, _key2, _key3, _body); break;
        }
    }
}
