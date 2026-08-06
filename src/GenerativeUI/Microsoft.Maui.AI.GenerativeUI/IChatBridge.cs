namespace Microsoft.Maui.AI.GenerativeUI;

/// <summary>
/// An intent raised by an interactive control back into the chat loop (rather than the control
/// calling a tool directly). The app's chat view model turns it into a synthetic user turn so the
/// model decides what to do next. See <c>docs/GenerativeUI/spec/appendix-ui-dsl.md §6</c>.
/// </summary>
/// <param name="Name">
/// The intent name: reserved <c>submit</c>/<c>confirm</c>/<c>cancel</c>, or <c>action:&lt;name&gt;</c>.
/// </param>
/// <param name="Payload">Optional opaque payload the model receives with the intent.</param>
public sealed record UiIntent(string Name, string? Payload = null);

/// <summary>
/// Implemented by the app's chat view model. The library raises intents from generated buttons and
/// the confirm overlay; the app posts a corresponding synthetic user turn so the loop stays
/// AI-driven.
/// </summary>
public interface IChatBridge
{
    /// <summary>Raise a UI intent into the chat loop.</summary>
    Task RaiseIntentAsync(UiIntent intent);
}
