# Copilot SDK

IChatClient adapter for the GitHub Copilot SDK — wraps `CopilotClient`/`CopilotSession` as a standard [`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient).

> ⚠️ **Experimental**: This product is part of [dotnet/maui-labs](https://github.com/dotnet/maui-labs) and may have breaking changes between releases.

## Features

- **`IChatClient` adapter** — use the GitHub Copilot SDK with any `IChatClient`-compatible code
- **Streaming** — real-time token streaming via `IAsyncEnumerable<ChatResponseUpdate>`
- **Tool calling** — native SDK tool execution; pass `AIFunction` tools via `ChatOptions`
- **Session management** — `ResetSessionAsync()` for fresh conversations
- **DI-friendly** — `AddCopilotSdkChatClient()` extension method
- **Thread-safe** — `SemaphoreSlim`-based lazy initialization

## Packages

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.CopilotSdk` | IChatClient adapter for the GitHub Copilot SDK |

## Requirements

- .NET 10 SDK
- GitHub Copilot CLI (`copilot`) or a valid GitHub token
- Active GitHub Copilot subscription

## Building

```bash
dotnet build src/CopilotSdk/CopilotSdk.slnf
```

## Testing

```bash
# Unit tests (no Copilot CLI needed)
dotnet test tests/CopilotSdk/Microsoft.Maui.CopilotSdk.Tests/ --filter "FullyQualifiedName~UnitTests|FullyQualifiedName~ConfigurationTests"

# Integration tests (requires Copilot CLI)
dotnet test tests/CopilotSdk/Microsoft.Maui.CopilotSdk.Tests/
```

## Architecture

```
src/CopilotSdk/
├── Microsoft.Maui.CopilotSdk/           # IChatClient adapter library
│   ├── CopilotSdkChatClient.cs          # IChatClient implementation
│   ├── CopilotSdkConfiguration.cs       # Configuration options
│   └── CopilotSdkServiceCollectionExtensions.cs  # DI registration
├── CopilotSdkSample/                    # MAUI sample app with chat UI
└── CopilotSdk.slnf                      # Solution filter
tests/CopilotSdk/
└── Microsoft.Maui.CopilotSdk.Tests/     # Unit + integration tests
```

## Sample App

The `CopilotSdkSample` is a minimal .NET MAUI app demonstrating:

- Basic chat with the Copilot SDK via `IChatClient`
- Streaming text responses displayed in a `CollectionView`
- Tool calling with a weather tool and a time tool

Run it:

```bash
dotnet build src/CopilotSdk/CopilotSdkSample/ -f net10.0-maccatalyst
dotnet run --project src/CopilotSdk/CopilotSdkSample/ -f net10.0-maccatalyst
```

## Quick Start

```csharp
// Register in DI
services.AddCopilotSdkChatClient(config =>
{
    config.Model = "gpt-4.1";
    config.UseLoggedInUser = true;
});

// Inject and use
public class MyService(IChatClient chatClient)
{
    public async Task<string> AskAsync(string question)
    {
        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, question)]);
        return response.Messages[0].Text!;
    }
}
```
