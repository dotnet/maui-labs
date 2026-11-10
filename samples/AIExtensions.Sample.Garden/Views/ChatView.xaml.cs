using AIExtensions.Sample.Garden.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace AIExtensions.Sample.Garden.Views;

public partial class ChatView : ContentView, IRecipient<ChatMessageAddedMessage>
{
    public ChatView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.Register(this);
    }

    void IRecipient<ChatMessageAddedMessage>.Receive(ChatMessageAddedMessage message)
    {
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
        {
            try { MessagesView.ScrollTo(message.Message, position: ScrollToPosition.End, animate: true); }
            catch { /* item may have been removed */ }
        });
    }
}
