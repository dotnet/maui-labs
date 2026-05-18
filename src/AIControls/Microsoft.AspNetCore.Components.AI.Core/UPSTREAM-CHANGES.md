# Upstream Changes

This project contains code copied from [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) branch `javiercn/ai-components-e2e-tests`, path `src/Components/AI/src/`.

**Goal**: Use this code as-is. When their package ships on NuGet, replace this project with a PackageReference.

## Modifications

<!-- List any modifications made to the upstream code here -->
<!-- Format: | File | Change | Reason | -->

| File | Change | Reason |
|------|--------|--------|
| (none yet) | | |

## Wanted Upstream

Features we'd like added to the core engine (document only, don't implement here):

- `AgentContext.Clear()` — Reset conversation (clear turns, history)
- `AgentContext.SystemPrompt` property — Sugar over `UIAgentOptions.ChatOptions`
- `AgentContext.AutoRejectPendingApprovals()` — Reject pending on new user message
- `AgentContext.HasPendingApprovals` computed property
- `UIAgentOptions.AllowMultipleToolCalls` convenience property
