using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Registers the AI chat controls with a .NET MAUI app.</summary>
public static class AppBuilderExtensions
{
    /// <summary>
    /// Registers the default attachment picker, audio recorder, and speech recognizer used by
    /// <see cref="AIChatView"/>. Services you register yourself (before or after) take precedence, and calling
    /// this more than once has no further effect.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Register an <see cref="IChatClient"/> as well and an <see cref="AIChatView"/> without a
    /// <see cref="AIChatView.Session"/> or <see cref="AIChatView.ChatClient"/> uses it. Register an
    /// <see cref="IChatAudioTranscriber"/> to make the audio button dictate instead of attaching recordings.
    /// </remarks>
    public static MauiAppBuilder UseAIChat(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IChatAttachmentPicker>(FileChatAttachmentPicker.Default);
        builder.Services.TryAddSingleton<IChatAudioRecorder, MauiChatAudioRecorder>();
        builder.Services.TryAddSingleton<IChatSpeechRecognizer, MauiChatSpeechRecognizer>();
        return builder;
    }
}
