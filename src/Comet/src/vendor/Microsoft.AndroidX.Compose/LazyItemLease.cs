using Android.Runtime;
using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

[Register("net/compose/LazyItemLease")]
internal sealed class LazyItemLease : Java.Lang.Object, IRememberObserver
{
    ComposableNode? _content;
    IDisposable? _lease;

    LazyItemLease(ComposableNode content, IDisposable lease)
    {
        _content = content;
        _lease = lease;
    }

    internal LazyItemLease(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer) { }

    internal static void Remember(IComposer composer, ComposableNode content, Func<IDisposable> acquire)
    {
        composer.StartReplaceableGroup(0x434c524c);
        try
        {
            if (composer.RememberedValue() is LazyItemLease existing &&
                ReferenceEquals(existing._content, content))
                return;

            // Store the observer directly, not inside RememberHolder: Compose must
            // receive OnAbandoned even when speculative composition never commits.
            var lease = acquire();
            ArgumentNullException.ThrowIfNull(lease);
            LazyItemLease? observer = null;
            try
            {
                observer = new LazyItemLease(content, lease);
                composer.UpdateRememberedValue(observer);
            }
            catch
            {
                if (observer is null)
                    lease.Dispose();
                else
                    observer.Release();
                throw;
            }
        }
        finally
        {
            composer.EndReplaceableGroup();
        }
    }

    public void OnRemembered() { }
    public void OnForgotten() => Release();
    public void OnAbandoned() => Release();

    void Release()
    {
        _content = null;
        Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}
