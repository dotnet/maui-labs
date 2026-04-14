
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace ControlGallery.Common.Messages;

public class ToggleFlyoutHeaderMsg : ValueChangedMessage<bool>
{
    public bool IsVisible {get;set;}

    public ToggleFlyoutHeaderMsg(bool value) : base(value)
    {
        IsVisible = value;
    }
}