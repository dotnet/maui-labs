using Microsoft.Maui.Hosting;
using Microsoft.Maui.Chat.Controls.Blazor;

namespace Microsoft.Maui.AI.Chat.Controls.Blazor;

/// <summary>Registers the AI Blazor chat controls with a MAUI application.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers the provider-neutral Blazor chat services required by
    /// <see cref="CopilotChatView"/>.
    /// </summary>
    /// <param name="builder">The MAUI application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static MauiAppBuilder AddAIChatControlsBlazor(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddChatControlsBlazor();
        return builder;
    }
}
