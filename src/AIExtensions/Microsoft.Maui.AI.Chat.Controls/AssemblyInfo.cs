// One XAML namespace for the block templates and the engine's block types, so a page can write
// xmlns:chat="http://schemas.microsoft.com/dotnet/2026/maui/ai/chat" and use both
// <chat:BlockTemplateSelector /> and {x:Type chat:FunctionInvocationContentBlock}.
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/2026/maui/ai/chat", "Microsoft.Maui.AI.Chat.Controls")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/2026/maui/ai/chat", "Microsoft.Maui.AI.Chat", AssemblyName = "Microsoft.Maui.AI.Chat")]
[assembly: Microsoft.Maui.Controls.XmlnsPrefix("http://schemas.microsoft.com/dotnet/2026/maui/ai/chat", "chat")]
