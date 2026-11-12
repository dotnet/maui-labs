// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Shows a <see cref="ToolApprovalBlock"/> as chrome, such as Approve and Reject buttons, around the view of the call it
/// asks about: its <see cref="InteractiveFunctionBlock.InnerBlock"/>, which is a call block, a typed block for the tool,
/// or a block of the app's own type.
/// </summary>
/// <remarks>
/// <para>
/// An approval that nothing shows can't be answered, and its turn waits until it is stopped, so give the selector an
/// approval wrapper, or a template that matches <see cref="ToolApprovalBlock"/>:
/// </para>
/// <code language="xml">
/// &lt;chat:BlockTemplateSelector.Wrappers&gt;
///     &lt;chat:ApprovalWrapper&gt;
///         &lt;ControlTemplate x:DataType="chat:ToolApprovalBlock"&gt;
///             &lt;VerticalStackLayout&gt;
///                 &lt;ContentPresenter /&gt;
///                 &lt;HorizontalStackLayout IsVisible="{Binding IsAwaitingInput}"&gt;
///                     &lt;Button Text="Approve" Clicked="OnApprove" /&gt;
///                     &lt;Button Text="Reject" Clicked="OnReject" /&gt;
///                 &lt;/HorizontalStackLayout&gt;
///             &lt;/VerticalStackLayout&gt;
///         &lt;/ControlTemplate&gt;
///     &lt;/chat:ApprovalWrapper&gt;
/// &lt;/chat:BlockTemplateSelector.Wrappers&gt;
/// </code>
/// </remarks>
public sealed class ApprovalWrapper : BlockWrapper
{
    /// <inheritdoc />
    public override ContentBlock? GetWrappedBlock(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return (block as ToolApprovalBlock)?.InnerBlock;
    }
}
