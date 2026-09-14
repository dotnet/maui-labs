# Microsoft.Maui.Chat

`Microsoft.Maui.Chat` provides renderer-neutral chat primitives: conversations,
messages, participants, content, attachments, drafts, service contracts, and
`ChatComposerController`.

It is an experimental package. Use `Microsoft.Maui.Chat.Controls` for native XAML rendering or
`Microsoft.Maui.Chat.Controls.Blazor` for Razor Hybrid rendering. Both renderers can bind the same
`ChatConversation` and `ChatComposerController`.

| Package | Purpose |
| --- | --- |
| `Microsoft.Maui.Chat` | Shared conversation and composer state |
| `Microsoft.Maui.Chat.Controls` | Native MAUI XAML renderer |
| `Microsoft.Maui.Chat.Controls.Blazor` | MAUI Blazor Hybrid renderer |

The core package requires .NET 10 only. Native rendering requires the .NET MAUI workload.
