using Microsoft.Extensions.Configuration;

namespace AIChat.Sample.Shared;

/// <summary>Execution mode for the local chat samples.</summary>
public enum ChatSampleMode
{
    Live,
    Record,
    Replay,
}

public static class ChatSampleModeParser
{
    public const string EnvironmentVariable = "MAUI_AI_CHAT_MODE";

    /// <summary>
    /// Gets the sample mode, preferring the process environment over <c>AI:Chat:Mode</c>.
    /// </summary>
    public static ChatSampleMode Parse(IConfiguration configuration, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        environment ??= Environment.GetEnvironmentVariable;
        var value = environment(EnvironmentVariable) ?? configuration["AI:Chat:Mode"] ?? "Replay";

        if (Enum.TryParse<ChatSampleMode>(value, ignoreCase: true, out var mode))
            return mode;

        throw new InvalidOperationException(
            $"AI chat mode '{value}' is invalid. Set {EnvironmentVariable} or AI:Chat:Mode to Live, Record, or Replay.");
    }
}
