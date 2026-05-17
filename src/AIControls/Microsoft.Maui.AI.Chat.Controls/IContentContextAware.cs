namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Implemented by a custom view to receive the <see cref="ContentContext"/>
/// when the approval card is shown. Works like MAUI's <c>IQueryAttributable</c>,
/// but keeps the framework logic on the view itself instead of relying on an ambient BindingContext.
/// </summary>
public interface IContentContextAware
{
    /// <summary>
    /// Called by the library when the content is applied.
    /// Use this to extract tool arguments and populate ViewModel properties.
    /// </summary>
    void ApplyContentContext(ContentContext context);
}
