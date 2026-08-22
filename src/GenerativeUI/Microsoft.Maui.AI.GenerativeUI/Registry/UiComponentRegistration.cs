using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace Microsoft.Maui.AI.GenerativeUI.Registry;

public sealed class UiComponentRegistration
{
    public required ComponentDescriptor Descriptor { get; init; }
    public required Type ComponentType { get; init; }
}
