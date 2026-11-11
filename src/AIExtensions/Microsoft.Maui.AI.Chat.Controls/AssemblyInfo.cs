using Microsoft.Maui.AI.Chat.Controls;

// One XAML namespace for the controls and the engine's block types, so a page can write
// xmlns:chat="http://schemas.microsoft.com/dotnet/2026/maui/ai/chat" and use both
// <chat:AIChatView /> and {x:Type chat:FunctionInvocationContentBlock}.
[assembly: XmlnsDefinition(AIChatView.XmlNamespace, "Microsoft.Maui.AI.Chat.Controls")]
[assembly: XmlnsDefinition(AIChatView.XmlNamespace, "Microsoft.Maui.AI.Chat", AssemblyName = "Microsoft.Maui.AI.Chat")]
[assembly: Microsoft.Maui.Controls.XmlnsPrefix(AIChatView.XmlNamespace, "chat")]
