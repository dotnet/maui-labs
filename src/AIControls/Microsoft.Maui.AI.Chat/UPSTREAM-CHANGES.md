# Upstream Changes

This project contains code copied from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) branch `javiercn/ai-components-e2e-tests`, path `src/Components/AI/src/`.

**Goal**: Use this code as-is. When their package ships on NuGet, replace this project with a PackageReference.

## Modifications

<!-- List any modifications made to the upstream code here -->
<!-- Format: | File | Change | Reason | -->

| File | Change | Reason |
|------|--------|--------|
| `Blocks/ContentBlock.cs` | `Id` setter changed from `internal` to `public` | Source generator emits handlers in consumer assemblies that need to set `Id` from `FunctionCallContent.CallId` |
| `Engine/AgentContext.cs` | Added `uninvokedToolBlocks.RemoveAll(b => b.Result is not null)` after streaming loop (line ~132) | Prevents double tool invocation when `FunctionInvokingChatClient` middleware already handled the call during streaming |

## Wanted Upstream

Features we'd like added to the core engine (document only, don't implement here):

- `AgentContext.Clear()` — Reset conversation (clear turns, history)
- `AgentContext.SystemPrompt` property — Sugar over `UIAgentOptions.ChatOptions`
- `AgentContext.AutoRejectPendingApprovals()` — Reject pending on new user message
- `AgentContext.HasPendingApprovals` computed property
- `UIAgentOptions.AllowMultipleToolCalls` convenience property
- Thread-safety on `_callbacks` lists (`ContentBlock._callbacks`, `AgentContext._statusChangedCallbacks`, etc.) — use `ImmutableList` or lock

## Porting Notes

- **MAUI uses `ContentTemplate.When()` instead of Blazor's `BlockRenderer<TBlock>`** for per-block-type template selection
- **MAUI provides richer tool rendering** than Blazor out-of-the-box (FunctionCallTemplate, FunctionResultTemplate with tool-name filtering)
- **No `AgentBoundary` cascading parameter equivalent** — MAUI uses explicit `Session` property on `CopilotChatView`
- **Tool invocation ownership**: `UseFunctionInvocation()` middleware handles the tool loop; Core's `UIAgent` does NOT drive tool calls directly when this middleware is present

