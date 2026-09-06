# Microsoft.Maui.CopilotSdk

An [`IChatClient`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.ichatclient) adapter for the [GitHub Copilot SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK). Wraps `CopilotClient`/`CopilotSession` as a standard `Microsoft.Extensions.AI` chat client with streaming, tool calling, and session management.

> ⚠️ **Experimental**: This package is part of [dotnet/maui-labs](https://github.com/dotnet/maui-labs) and may have breaking changes between releases.

## Install

```bash
dotnet add package Microsoft.Maui.CopilotSdk
```

## Quick Start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.CopilotSdk;

// Create the client
var client = new CopilotSdkChatClient(new CopilotSdkConfiguration
{
    Model = "gpt-4.1",
    UseLoggedInUser = true,
});

// Send a message and get a response
var response = await client.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "Hello!")]);
Console.WriteLine(response.Messages[0].Text);

// Stream responses
await foreach (var update in client.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "Tell me a joke")]))
{
    foreach (var text in update.Contents.OfType<TextContent>())
        Console.Write(text.Text);
}
```

## DI Registration

```csharp
services.AddCopilotSdkChatClient(config =>
{
    config.Model = "gpt-4.1";
    config.UseLoggedInUser = true;
    config.CliPath = "/opt/homebrew/bin/copilot";
});

// Inject IChatClient anywhere
public class MyService(IChatClient chatClient) { }
```

## Tool Calling

The Copilot SDK handles tool invocation natively. Pass tools via `ChatOptions`:

```csharp
var tools = new List<AITool>
{
    AIFunctionFactory.Create(() => "22°C, Sunny",
        name: "get_weather",
        description: "Get the current weather"),
};

await foreach (var update in client.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "What's the weather?")],
    new ChatOptions { Tools = tools }))
{
    // Tool is invoked by the SDK automatically
}
```

## Features

| Feature | Details |
|---------|---------|
| **Streaming** | Real-time token streaming via `IAsyncEnumerable<ChatResponseUpdate>` |
| **Tool calling** | Native SDK tool execution — pass `AIFunction` tools via `ChatOptions` |
| **Session management** | `ResetSessionAsync()` to start fresh conversations |
| **DI-friendly** | `AddCopilotSdkChatClient()` extension method |
| **Thread-safe** | `SemaphoreSlim`-based lazy initialization |
| **Configurable timeout** | `StreamingTimeout` property (default: 5 minutes) |

## Platform Support

| Platform | Support |
|----------|---------|
| .NET 10+ | ✅ |

## Requirements

- .NET 10 SDK
- GitHub Copilot CLI (`copilot`) or a valid GitHub token
- Active GitHub Copilot subscription
