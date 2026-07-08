namespace AIExtensions.Sample.Garden.Views;

/// <summary>
/// Hosts the single <c>CopilotChatView</c> declared in <c>ChatView.xaml</c>. The handler axis is driven
/// by the view model (it recreates its <c>Session</c>), and the rendering axis is driven declaratively by
/// the <c>ChatTemplateStyle</c> data trigger on <c>IsPreview</c> — so there is nothing to wire up here.
/// </summary>
public partial class ChatView : ContentView
{
    public ChatView()
    {
        InitializeComponent();
    }
}
