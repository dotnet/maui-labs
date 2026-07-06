namespace AIExtensions.Sample.Garden.Views;

/// <summary>
/// Hosts the two chat controls declared in <c>ChatView.xaml</c> — the full <c>CopilotChatView</c>
/// (fancy) and the bare <c>MessageListView</c> (plain) — which are shown/hidden by binding their
/// visibility to the view model's <c>IsFancy</c> / <c>IsPlain</c>. Templates and the toggle live in
/// XAML and the view model, so there is nothing to wire up here.
/// </summary>
public partial class ChatView : ContentView
{
    public ChatView()
    {
        InitializeComponent();
    }
}
