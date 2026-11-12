// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Shows a block that wraps another, such as an approval and the call it asks about, as chrome around the wrapped
/// block's own view. <see cref="Template"/> is the chrome, and its <see cref="ContentPresenter"/> shows the view that
/// the <see cref="BlockTemplateSelector"/> picks for the wrapped block.
/// </summary>
/// <remarks>
/// <para>
/// Add wrappers to <see cref="BlockTemplateSelector.Wrappers"/>. The selector tries them before its templates, uses
/// the first that wraps a block, and picks the wrapped block's template as it picks any block's, so a card written
/// for a call shows the same inside an approval. The chrome's <see cref="BindableObject.BindingContext"/> is the
/// wrapping block (for <see cref="ApprovalWrapper"/>, the <see cref="ToolApprovalBlock"/>), and the wrapped block's
/// view binds to the wrapped block. A view inside the wrapped block's view reaches the wrapping block with a
/// <see cref="RelativeBindingSource"/> whose <see cref="RelativeBindingSource.AncestorType"/> is the wrapping
/// block's type.
/// </para>
/// <para>
/// The selector creates one template for each pair of chrome and wrapped block template, and returns it every time,
/// so a list that caches its item views by template (as <see cref="CollectionView"/> does) reuses the whole view.
/// </para>
/// </remarks>
[ContentProperty(nameof(Template))]
public abstract class BlockWrapper : BindableObject
{
    /// <summary>Backing property for <see cref="When"/>.</summary>
    public static readonly BindableProperty WhenProperty =
        BindableProperty.Create(nameof(When), typeof(BlockMatch), typeof(BlockWrapper));

    /// <summary>Backing property for <see cref="Template"/>.</summary>
    public static readonly BindableProperty TemplateProperty =
        BindableProperty.Create(nameof(Template), typeof(ControlTemplate), typeof(BlockWrapper));

    /// <summary>
    /// Gets or sets the wrapped blocks this wrapper applies to, matched against the wrapped block, such as
    /// <c>{chat:Tool delete_file}</c> for the approvals of that tool's calls. The block inside an approval shows the call
    /// the approval asks about, so a <see cref="ToolMatch"/> matches it by that call's tool, also when it is a block of the
    /// app's own type. When it isn't set, the wrapper applies to every block it wraps.
    /// </summary>
    public BlockMatch? When
    {
        get => (BlockMatch?)GetValue(WhenProperty);
        set => SetValue(WhenProperty, value);
    }

    /// <summary>
    /// Gets or sets the chrome around the wrapped block's view, with a <see cref="ContentPresenter"/> where that view
    /// goes. Without one, the wrapped block's view shows alone, still bound to the wrapped block. In XAML, it is the
    /// element's content.
    /// </summary>
    public ControlTemplate? Template
    {
        get => (ControlTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>Gets the block that <paramref name="block"/> wraps, or <see langword="null"/> when this wrapper doesn't wrap it.</summary>
    /// <param name="block">The block to show.</param>
    /// <returns>
    /// The wrapped block, which must not be <paramref name="block"/> itself, or <see langword="null"/>.
    /// </returns>
    public abstract ContentBlock? GetWrappedBlock(ContentBlock block);
}
