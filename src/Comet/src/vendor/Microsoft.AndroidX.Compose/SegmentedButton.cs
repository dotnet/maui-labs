using AndroidX.Compose.Runtime;

namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>SegmentedButton</c> — a radio-button-shaped selector
/// segment. Must be a child of a
/// <see cref="SingleChoiceSegmentedButtonRow"/> or
/// <see cref="MultiChoiceSegmentedButtonRow"/>; the parent publishes its
/// scope receiver via <see cref="RenderContext"/>.
///
/// <para>Use the <c>onClick</c> ctor for the single-choice (radio) flavor
/// and the <c>onCheckedChange</c> ctor for the multi-choice (toggle)
/// flavor — the matching scope must already be on the stack.</para>
///
/// <para>Children are rendered as the <c>label</c> slot (required). Set
/// <see cref="Icon"/> via object-initializer for an optional leading
/// indicator.</para>
/// <code>
/// new SingleChoiceSegmentedButtonRow
/// {
///     new SegmentedButton(selected: tab == 0, onClick: () =&gt; tab.Value = 0) { new Text("Day") },
///     new SegmentedButton(selected: tab == 1, onClick: () =&gt; tab.Value = 1) { new Text("Week") },
/// }
/// </code>
/// </summary>
public sealed class SegmentedButton : ComposableContainer
{
    readonly bool _selected;
    readonly Action? _onClick;
    readonly Action<bool>? _onCheckedChange;

    /// <summary>Single-choice (radio) ctor for <see cref="SingleChoiceSegmentedButtonRow"/>.</summary>
    public SegmentedButton(bool selected, Action onClick)
    {
        _selected = selected;
        _onClick  = onClick;
    }

    /// <summary>Multi-choice (toggle) ctor for <see cref="MultiChoiceSegmentedButtonRow"/>.</summary>
    public SegmentedButton(bool @checked, Action<bool> onCheckedChange)
    {
        _selected        = @checked;
        _onCheckedChange = onCheckedChange;
    }

    /// <summary>Optional leading icon slot.</summary>
    public ComposableNode? Icon { get; set; }

    public override void Render(IComposer composer)
    {
        if (Children.Count == 0)
            throw new InvalidOperationException(
                "SegmentedButton requires at least one child (the label slot has no Kotlin default).");

        var label = ComposableLambdas.Wrap2(composer, c =>
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                c.StartReplaceableGroup(HashCode.Combine(i, child.GetType()));
                try { child.Render(c); }
                finally { c.EndReplaceableGroup(); }
            }
        });
        var iconNode = Icon;
        var icon = iconNode is null ? null : ComposableLambdas.Wrap2(composer, c => iconNode.Render(c));
        var modifier = BuildModifier();

        var scope = RenderContext.CurrentScope;
        var count = RenderContext.CurrentRowChildCount;
        var index = RenderContext.CurrentRowChildIndex;
        if (scope == IntPtr.Zero || count == 0)
            throw new InvalidOperationException(
                "SegmentedButton must be rendered inside a SingleChoiceSegmentedButtonRow " +
                "or MultiChoiceSegmentedButtonRow — no row scope/position is on the stack.");

        // Kotlin signature has no default for `shape`, so we must supply
        // one. SegmentedButtonDefaults.itemShape returns the correct
        // start/middle/end rounded-corner shape based on (index, count).
        var shape = ComposeBridges.ItemShape(index, count, composer);

        if (_onCheckedChange is not null)
        {
            int defaults = (int)MultiChoiceSegmentedButtonDefault.All;
            if (modifier is not null) defaults &= ~(int)MultiChoiceSegmentedButtonDefault.Modifier;
            if (icon     is not null) defaults &= ~(int)MultiChoiceSegmentedButtonDefault.Icon;

            var cb = new ComposableLambda1(arg =>
            {
                if (arg is not Java.Lang.Boolean jb)
                    throw new InvalidOperationException(
                        $"MultiChoiceSegmentedButton.onCheckedChange expected a Java.Lang.Boolean, got '{arg?.GetType().FullName ?? "null"}'.");
                _onCheckedChange(jb.BooleanValue());
            });
            ComposeBridges.MultiChoiceSegmentedButton(
                multiChoiceScope: scope,
                @checked:         _selected,
                onCheckedChange:  cb,
                shape:            shape,
                label:            label,
                modifier:         modifier,
                icon:             icon,
                defaults:         defaults,
                composer:         composer);
        }
        else
        {
            int defaults = (int)SingleChoiceSegmentedButtonDefault.All;
            if (modifier is not null) defaults &= ~(int)SingleChoiceSegmentedButtonDefault.Modifier;
            if (icon     is not null) defaults &= ~(int)SingleChoiceSegmentedButtonDefault.Icon;

            var click = new ComposableLambda0(_onClick!);
            ComposeBridges.SingleChoiceSegmentedButton(
                singleChoiceScope: scope,
                selected:          _selected,
                onClick:           click,
                shape:             shape,
                label:             label,
                modifier:          modifier,
                icon:              icon,
                defaults:          defaults,
                composer:          composer);
        }
    }
}
