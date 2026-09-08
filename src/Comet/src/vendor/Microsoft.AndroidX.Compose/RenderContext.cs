namespace AndroidX.Compose;

/// <summary>
/// Thread-static stash of the current Compose receiver scope (RowScope /
/// ColumnScope) handle. Set by container composables that consume a
/// scope-receiver Function3 content lambda; read by *Item composables
/// whose underlying Kotlin static method takes the scope as its first
/// argument. Composition runs synchronously on a single thread, so a
/// <c>[ThreadStatic]</c> is sufficient — and a struct-disposable scope
/// guard keeps push/pop balanced even when child <c>Render</c> throws.
/// </summary>
internal static class RenderContext
{
    [ThreadStatic]
    static IntPtr s_scope;

    [ThreadStatic]
    static ScopeKind s_scopeKind;

    [ThreadStatic]
    static int s_rowChildIndex;

    [ThreadStatic]
    static int s_rowChildCount;

    public static IntPtr CurrentScope => s_scope;

    /// <summary>
    /// Kind of the currently-active scope receiver, or
    /// <see cref="ScopeKind.None"/> when no scope has been pushed.
    /// Read by scope-extension modifiers (e.g. <c>Modifier.Weight</c>)
    /// to pick the right Kotlin helper.
    /// </summary>
    public static ScopeKind CurrentScopeKind => s_scopeKind;

    /// <summary>
    /// Index of the currently-rendering child within an enclosing row
    /// container. Read by composables (<see cref="SegmentedButton"/>)
    /// whose Kotlin defaults depend on position (e.g. start/end shape).
    /// Only meaningful while <see cref="CurrentRowChildCount"/> &gt; 0.
    /// </summary>
    public static int CurrentRowChildIndex => s_rowChildIndex;

    /// <summary>Total child count of the enclosing row container, or 0.</summary>
    public static int CurrentRowChildCount => s_rowChildCount;

    public static ScopeFrame PushScope(IntPtr scope, ScopeKind kind = ScopeKind.Other)
    {
        var prevScope = s_scope;
        var prevKind  = s_scopeKind;
        s_scope     = scope;
        s_scopeKind = kind;
        return new ScopeFrame(prevScope, prevKind);
    }

    /// <summary>
    /// Publish row-position state for child composables. Returns a frame
    /// that restores the previous values on dispose. The row container
    /// calls <see cref="RowFrame.SetIndex"/> before each child Render.
    /// </summary>
    public static RowFrame PushRow(int count)
    {
        var prevIndex = s_rowChildIndex;
        var prevCount = s_rowChildCount;
        s_rowChildCount = count;
        s_rowChildIndex = 0;
        return new RowFrame(prevIndex, prevCount);
    }

    internal readonly struct ScopeFrame : IDisposable
    {
        readonly IntPtr    _previousScope;
        readonly ScopeKind _previousKind;
        public ScopeFrame(IntPtr previousScope, ScopeKind previousKind)
        {
            _previousScope = previousScope;
            _previousKind  = previousKind;
        }
        public void Dispose()
        {
            s_scope     = _previousScope;
            s_scopeKind = _previousKind;
        }
    }

    internal readonly struct RowFrame : IDisposable
    {
        readonly int _prevIndex;
        readonly int _prevCount;
        public RowFrame(int prevIndex, int prevCount)
        {
            _prevIndex = prevIndex;
            _prevCount = prevCount;
        }
        public void SetIndex(int index) => s_rowChildIndex = index;
        public void Dispose()
        {
            s_rowChildIndex = _prevIndex;
            s_rowChildCount = _prevCount;
        }
    }
}
