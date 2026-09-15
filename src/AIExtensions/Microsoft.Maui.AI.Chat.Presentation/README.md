# Microsoft.Maui.AI.Chat.Presentation

`Microsoft.Maui.AI.Chat.Presentation` projects an `AgentContext` into the provider-neutral
`ChatConversation` model used by MAUI and Blazor chat controls. It is experimental.

```csharp
using var presentation = new AgentChatPresentation(agentContext);
```

The presentation owns only its subscriptions and projected messages; it never disposes the
supplied `AgentContext`.
