using Microsoft.Maui.AI.Chat.Controls.Blazor;

namespace Microsoft.Maui.AI.Chat.Controls.Blazor.Tests;

public class PresentationComponentTests
{
    [Fact]
    public void CopilotChatView_IsAComponent()
    {
        Assert.IsAssignableFrom<Microsoft.AspNetCore.Components.ComponentBase>(
            new CopilotChatView());
    }
}
