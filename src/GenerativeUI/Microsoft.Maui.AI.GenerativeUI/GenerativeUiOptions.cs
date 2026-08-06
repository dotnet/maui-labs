using Microsoft.Maui.AI.GenerativeUI.OpenApi;
using Microsoft.Maui.AI.GenerativeUI.Registry;

namespace Microsoft.Maui.AI.GenerativeUI;

/// <summary>
/// App-owned configuration for the whole Generative UI stack: the OpenAPI server-API settings
/// (inherited from <see cref="GenerativeOpenApiOptions"/>) plus the <see cref="Ui"/> registry of
/// app-specific styles, controls, and screens. The model never sees any of this.
/// </summary>
public sealed class GenerativeUiOptions : GenerativeOpenApiOptions
{
    /// <summary>
    /// The registry of app-registered styles/controls/screens. Configure it here at startup
    /// (<c>options.Ui.AddStyle(...)</c>) and/or resolve <see cref="GenerativeUiRegistry"/> from DI to
    /// change it later.
    /// </summary>
    public GenerativeUiRegistry Ui { get; } = new();
}
