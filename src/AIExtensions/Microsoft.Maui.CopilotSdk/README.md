# Microsoft.Maui.CopilotSdk

An experimental [`Microsoft.Extensions.AI.IChatClient`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) adapter for the [GitHub Copilot SDK](https://github.com/github/copilot-sdk). It lets you drive the GitHub Copilot runtime through the same `IChatClient` abstraction used across the .NET AI ecosystem, with streaming, stateful conversations, and full Microsoft.Extensions.AI tool-calling support.

> **Experimental.** This package is `0.1.0-preview`. APIs may change.

## Features

- **`IChatClient` adapter** over `CopilotClient` with streaming (`GetStreamingResponseAsync`) and aggregation (`GetResponseAsync`).
- **Stateful conversations.** Completed turns surface the Copilot session id as `ChatResponse.ConversationId`. Pass it back via `ChatOptions.ConversationId` to continue the conversation. A session remains active only while an external tool waits for its M.E.AI result.
- **Microsoft.Extensions.AI tool loop.** Tools you pass through `ChatOptions.Tools` are represented by SDK proxy functions. The proxy surfaces `FunctionCallContent`, waits while `FunctionInvokingChatClient` invokes your real function, then resumes the same SDK session with the supplied result. The original function is never double-invoked.
- **Safe by default.** Only the tools you supply are available — built-in file/shell tools are excluded, and permission requests are denied unless they target one of your tools. Override with a custom permission policy.
- **Reasoning, usage, and metadata.** Reasoning deltas map to `TextReasoningContent` (never folded into the answer text), usage maps to `UsageContent`/`UsageDetails`, and model/response/message ids and finish reasons are preserved.
- **Robust lifecycle.** Caller cancellation stays `OperationCanceledException`; a streaming inactivity timeout becomes `TimeoutException`; in-flight work is aborted on cancellation/timeout; sessions are always disposed (durable state is preserved for resumption).

## Requirements

- .NET 10
- The GitHub Copilot CLI runtime (provided by the `GitHub.Copilot.SDK` package for your app, or point `CopilotSdkConfiguration.CliPath` at an installed `copilot`).

## Quick start

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Maui.CopilotSdk;

await using var client = new CopilotSdkChatClient(new CopilotSdkConfiguration
{
    Model = "gpt-5",
    SystemInstructions = "You are a helpful assistant.",
});

// Streaming
await foreach (var update in client.GetStreamingResponseAsync(
    [new ChatMessage(ChatRole.User, "Write a haiku about the sea.")]))
{
    Console.Write(update.Text);
}
```

### Continue a conversation

```csharp
var first = await client.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "My name is Ada.")]);

var second = await client.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "What is my name?")],
    new ChatOptions { ConversationId = first.ConversationId });
```

### Tool calling with FunctionInvokingChatClient

```csharp
using System.ComponentModel;

[Description("Gets the weather for a city.")]
static string GetWeather([Description("City name")] string city) => $"Sunny in {city}";

await using var copilot = new CopilotSdkChatClient(new CopilotSdkConfiguration());
using IChatClient chat = new ChatClientBuilder(copilot)
    .UseFunctionInvocation()
    .Build();

var response = await chat.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "What's the weather in Paris?")],
    new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeather)] });
```

The `get_weather` function runs in your process — the runtime requests it, the Microsoft.Extensions.AI tool loop invokes it, and the result is fed back to the model automatically.

### Dependency injection

```csharp
services.AddCopilotSdkChatClient(config =>
{
    config.Model = "gpt-5";
    config.SystemInstructions = "You are concise.";
});
```

## Configuration

| Property | Description |
| --- | --- |
| `Model` | Default model id. `null` uses the runtime default. Overridden by `ChatOptions.ModelId`. |
| `SystemInstructions` | System message prepended to conversations, combined with `ChatOptions.Instructions` and system/developer messages. |
| `ReasoningEffort` | Default reasoning effort (`low`/`medium`/`high`/`xhigh`/`max`). Overridden by `ChatOptions.Reasoning`. |
| `UseLoggedInUser` | Authenticate as the logged-in GitHub user. Ignored when `GitHubToken` is set. |
| `GitHubToken` | Explicit GitHub token. |
| `CliPath` / `CliArguments` | Path (and args) to the Copilot CLI; connects via stdio. |
| `WorkingDirectory` / `BaseDirectory` | Session working directory and `COPILOT_HOME`. |
| `PermissionHandler` | Custom permission policy. Defaults to a safe policy that only approves your supplied tools. |
| `StreamingInactivityTimeout` | Time to wait between events before a stream is considered stalled (default 5 minutes). |

## Supported and unsupported options

**Mapped:** `ChatOptions.ModelId`, `Instructions`, `Reasoning` (effort), `ResponseFormat` (JSON), `Tools`, `ToolMode` (`Auto`/`None`), `ConversationId`, and image attachments (raw bytes and `data:` URIs).

Required-tool modes throw `NotSupportedException` because the Copilot SDK has no equivalent.

**Ignored (no Copilot SDK equivalent):** `Temperature`, `MaxOutputTokens`, `TopP`, `TopK`, `FrequencyPenalty`, `PresencePenalty`, `StopSequences`, `Seed`.

## Concurrency

A single `CopilotSdkChatClient` is intended to be driven by one logical caller sequence at a time (as `FunctionInvokingChatClient` and typical chat loops do). It holds no locks and retains only short-lived pending proxy sessions between a tool request and its result. Use separate instances (or serialize calls) for concurrent conversations.

Completed conversation turns are durable through `ConversationId`. A tool call that is currently waiting for its .NET result is process-local and must complete on the same `CopilotSdkChatClient` instance; abandoning the instance abandons that pending tool turn.
