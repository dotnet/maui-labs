namespace AndroidX.Compose;

/// <summary>
/// Fluent receiver passed to
/// <see cref="Modifier.Semantics(Action{SemanticsScope})"/> and
/// its overloads. Mirrors Kotlin's
/// <c>androidx.compose.ui.semantics.SemanticsPropertyReceiver</c> —
/// the lambda-receiver type Kotlin code sees as
/// <c>Modifier.semantics { selected = true; role = Role.Tab; ... }</c>.
///
/// Each method invokes the corresponding
/// <c>SemanticsPropertiesKt</c> setter against the underlying
/// JNI <c>SemanticsPropertyReceiver</c> handle and returns
/// <c>this</c>, so a single call can chain several properties:
///
/// <code>
/// Modifier.Semantics(s =&gt; s
///     .Selected(isSelected)
///     .Role(SemanticsRole.Tab)
///     .ContentDescription("Email from " + sender)
///     .OnClick("Open email", () =&gt; { open(); return true; }));
/// </code>
///
/// The scope is short-lived — Compose may invoke the configuration
/// lambda multiple times (e.g. when semantics are re-applied), and
/// each invocation gets a fresh <see cref="SemanticsScope"/> bound to
/// the JNI handle that's live for that call. After the user's
/// <c>Action&lt;SemanticsScope&gt;</c> returns the scope is
/// invalidated; capturing it and calling a method later throws
/// <see cref="ObjectDisposedException"/>.
/// </summary>
public sealed class SemanticsScope
{
    IntPtr _receiver;
    readonly int _threadId;
    bool _active;

    internal SemanticsScope(IntPtr receiver)
    {
        _receiver = receiver;
        _threadId = Environment.CurrentManagedThreadId;
        _active = true;
    }

    internal void Invalidate()
    {
        _active = false;
        _receiver = IntPtr.Zero;
    }

    IntPtr Handle
    {
        get
        {
            if (!_active)
                throw new ObjectDisposedException(nameof(SemanticsScope),
                    "SemanticsScope can only be used inside the Action<SemanticsScope> " +
                    "callback passed to Modifier.Semantics(...) / ClearAndSetSemantics(...). " +
                    "Capturing the scope and calling a method later is not supported.");
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new InvalidOperationException(
                    "SemanticsScope must be used on the same managed thread that received it. " +
                    "Compose invokes the builder callback synchronously on the composition thread; " +
                    "do not dispatch SemanticsScope calls to a Task / Thread / Dispatcher.");
            return _receiver;
        }
    }

    /// <summary>
    /// <c>heading()</c> — tags this node as a heading. TalkBack
    /// announces "heading" alongside the content description and lets
    /// users navigate between headings via the rotor / swipe gestures.
    /// Compose's Foundation baseline is a single boolean; we don't
    /// expose per-level headings.
    /// </summary>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope Heading()
    {
        ComposeBridges.SemanticsSetHeading(Handle);
        return this;
    }

    /// <summary>
    /// <c>testTagsAsResourceId = value</c> — opts this subtree into
    /// surfacing <see cref="Modifier.TestTag(string)"/> through
    /// Android's <c>AccessibilityNodeInfo.viewIdResourceName</c>.
    /// Without this flag, UIAutomator / Espresso / Appium-Android
    /// can't find Compose nodes via <c>By.id(...)</c> — the testTag
    /// lives only in Compose's semantics tree, invisible to the
    /// platform a11y APIs that automation harnesses read.
    /// MAUI Compose-folded handlers set this on every node carrying
    /// an <c>AutomationId</c> so existing Appium <c>FindByAutomationId</c>
    /// tests keep working. Property is annotated
    /// <c>@ExperimentalComposeUiApi</c> upstream but stable in
    /// practice — it's been the canonical Compose-↔-Espresso bridge
    /// since 2021.
    /// </summary>
    /// <param name="value">Whether testTags on this node and its
    /// merged descendants should be exposed as resource IDs.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope TestTagsAsResourceId(bool value)
    {
        ComposeBridges.SemanticsSetTestTagsAsResourceId(Handle, value);
        return this;
    }

    /// <summary>
    /// <c>selected = isSelected</c> — marks the node as part of a
    /// selectable group (tab, list item, chip). TalkBack announces
    /// "selected" / "not selected" alongside the node's content
    /// description.
    /// </summary>
    /// <param name="isSelected">Whether this node is currently selected.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope Selected(bool isSelected)
    {
        ComposeBridges.SemanticsSetSelected(Handle, isSelected);
        return this;
    }

    /// <summary>
    /// <c>role = role</c> — tags the node with an accessibility
    /// <see cref="SemanticsRole"/> (e.g.
    /// <see cref="SemanticsRole.Tab"/>) so TalkBack announces the
    /// element class. Useful when wrapping a custom composable that
    /// behaves like a button/tab/etc. but isn't the real
    /// <see cref="Button"/> / <see cref="Tab"/> control.
    /// </summary>
    /// <param name="role">The accessibility role to advertise.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope Role(SemanticsRole role)
    {
        ComposeBridges.SemanticsSetRole(Handle, (int)role);
        return this;
    }

    /// <summary>
    /// <c>contentDescription = description</c> — the human-readable
    /// label TalkBack reads aloud when the node is focused. For
    /// graphical elements (icons, images) this is the only way the
    /// user knows what the node is.
    /// </summary>
    /// <param name="description">Non-null description text.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope ContentDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ComposeBridges.SemanticsSetContentDescription(Handle, description);
        return this;
    }

    /// <summary>
    /// <c>stateDescription = description</c> — a short string
    /// describing the node's current state, read after the content
    /// description (e.g. "expanded", "3 of 5"). Use for nodes whose
    /// announced label changes with state but whose identity does
    /// not.
    /// </summary>
    /// <param name="description">Non-null state description text.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope StateDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        ComposeBridges.SemanticsSetStateDescription(Handle, description);
        return this;
    }

    /// <summary>
    /// <c>onClick(label = label) { action() }</c> — registers a
    /// custom accessibility click action with a label TalkBack
    /// announces in the actions menu (e.g. "Open email"). The
    /// <paramref name="action"/> returns <c>true</c> if the action
    /// was handled, <c>false</c> to let the system fall back to the
    /// node's default click behavior.
    /// </summary>
    /// <param name="label">Action label, or <c>null</c> to use the
    /// platform-default label ("activate").</param>
    /// <param name="action">Callback invoked when the user triggers
    /// the action from the a11y menu. Must return whether the action
    /// was handled.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope OnClick(string? label, Func<bool> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var f0 = new ObjectFunction0(() => action()
            ? (Java.Lang.Object)Java.Lang.Boolean.True!
            : Java.Lang.Boolean.False!);
        ComposeBridges.SemanticsOnClick(Handle, label, f0);
        return this;
    }

    /// <summary>
    /// Convenience overload of
    /// <see cref="OnClick(string?, Func{bool})"/> that always
    /// reports the action as handled (returns <c>true</c>). Use when
    /// you don't need to defer back to the system's default click.
    /// </summary>
    /// <param name="label">Action label, or <c>null</c> for default.</param>
    /// <param name="action">Callback invoked when the user triggers
    /// the action.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope OnClick(string? label, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return OnClick(label, () => { action(); return true; });
    }

    /// <summary>
    /// Label-less overload of
    /// <see cref="OnClick(string?, Func{bool})"/> — registers
    /// the action with the platform-default label ("activate").
    /// </summary>
    /// <param name="action">Callback returning whether the action was handled.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope OnClick(Func<bool> action) => OnClick(label: null, action);

    /// <summary>
    /// Label-less <see cref="Action"/> overload of
    /// <see cref="OnClick(string?, Func{bool})"/> — always
    /// reports the action as handled.
    /// </summary>
    /// <param name="action">Callback invoked when the user triggers the action.</param>
    /// <returns>This scope, to chain further calls.</returns>
    public SemanticsScope OnClick(Action action) => OnClick(label: null, action);
}
