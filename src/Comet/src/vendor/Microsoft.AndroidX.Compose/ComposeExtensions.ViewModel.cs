using System.Runtime.CompilerServices;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

public static partial class ComposeExtensions
{
    /// <summary>
    /// Acquires (or creates on first composition) the
    /// <see cref="AndroidX.Compose.ViewModel"/> for this call site — the C#
    /// parity of Kotlin's
    /// <c>androidx.lifecycle.viewmodel.compose.viewModel&lt;T&gt;(…)</c>.
    /// </summary>
    /// <typeparam name="T">A <see cref="AndroidX.Compose.ViewModel"/> subclass.</typeparam>
    /// <param name="composer">The active composer.</param>
    /// <param name="factory">
    /// Constructs the view model the first time the host's
    /// <see cref="AndroidX.Lifecycle.ViewModelStore"/> sees this
    /// call site's storage key.
    /// </param>
    /// <param name="line">Auto-populated; do not pass.</param>
    /// <param name="file">Auto-populated; do not pass.</param>
    /// <remarks>
    /// The view model is owned by the nearest
    /// <see cref="AndroidX.Lifecycle.IViewModelStoreOwner"/> on
    /// <c>LocalViewModelStoreOwner</c> — the host
    /// <see cref="AndroidX.Activity.ComponentActivity"/> at the root,
    /// or the current
    /// <see cref="AndroidX.Navigation.NavBackStackEntry"/> inside a
    /// <see cref="NavHost"/>.
    /// </remarks>
    public static T ViewModel<T>(
        this IComposer composer,
        Func<T> factory,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        where T : ViewModel
        => ViewModelCore(composer, factory, keys: null, line, file);

    /// <summary>
    /// Keyed <c>viewModel(key1)</c>: the storage key includes the
    /// stringified <paramref name="key1"/>.
    /// </summary>
    public static T ViewModel<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        where T : ViewModel
        => ViewModelCore(composer, factory, new[] { key1 }, line, file);

    /// <summary>Keyed <c>viewModel(key1, key2)</c>.</summary>
    public static T ViewModel<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        object? key2,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        where T : ViewModel
        => ViewModelCore(composer, factory, new[] { key1, key2 }, line, file);

    /// <summary>Keyed <c>viewModel(key1, key2, key3)</c>.</summary>
    public static T ViewModel<T>(
        this IComposer composer,
        Func<T> factory,
        object? key1,
        object? key2,
        object? key3,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        where T : ViewModel
        => ViewModelCore(composer, factory, new[] { key1, key2, key3 }, line, file);

    /// <summary>
    /// Array-form keyed <c>viewModel(vararg keys)</c>.
    /// </summary>
    public static T ViewModelKeyed<T>(
        this IComposer composer,
        Func<T> factory,
        object?[] keys,
        [CallerLineNumber] int line = 0,
        [CallerFilePath] string file = "")
        where T : ViewModel
        => ViewModelCore(composer, factory, keys ?? throw new ArgumentNullException(nameof(keys)), line, file);

    static T ViewModelCore<T>(IComposer composer, Func<T> factory, object?[]? keys, int line, string file)
        where T : ViewModel
    {
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(factory);

        var ownerHandle = ComposeBridges.LocalViewModelStoreOwnerCurrent(composer);
        if (ownerHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "ViewModel<T> requires LocalViewModelStoreOwner to be set. " +
                "Call from inside ComponentActivity.SetContent or a NavHost destination so the host owner is in scope.");
        }
        var owner = Java.Lang.Object.GetObject<AndroidX.Lifecycle.IViewModelStoreOwner>(
            ownerHandle, Android.Runtime.JniHandleOwnership.TransferLocalRef)
            ?? throw new InvalidOperationException(
                "LocalViewModelStoreOwner.current returned a non-IViewModelStoreOwner handle.");

        var key = BuildViewModelKey(typeof(T), file, line, keys);
        var modelClass = Java.Lang.Class.FromType(typeof(T));
        var lambdaFactory = new LambdaViewModelFactory(factory);
        var provider = new AndroidX.Lifecycle.ViewModelProvider(owner, lambdaFactory);
        try
        {
            var vm = provider.Get(key, modelClass);
            return (T)vm;
        }
        finally
        {
            lambdaFactory.Dispose();
        }
    }

    static string BuildViewModelKey(Type type, string file, int line, object?[]? keys)
    {
        var sb = new System.Text.StringBuilder("net.compose:")
            .Append(type.FullName)
            .Append(':').Append(file)
            .Append(':').Append(line);
        if (keys is { Length: > 0 })
        {
            for (int i = 0; i < keys.Length; i++)
            {
                sb.Append('|');
                if (keys[i] is { } k) sb.Append(k);
                else sb.Append("<null>");
            }
        }
        return sb.ToString();
    }
}
