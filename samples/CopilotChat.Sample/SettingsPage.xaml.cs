using Microsoft.Maui.CopilotChat;

namespace CopilotChat.Sample;

public partial class SettingsPage : ContentPage
{
    private readonly CopilotChatConfiguration _config;

    public SettingsPage(CopilotChatConfiguration config)
    {
        InitializeComponent();
        _config = config;

        // Load current values
        SystemPromptEditor.Text = config.SystemMessage ?? "";
        ModelPicker.SelectedItem = config.Model;
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        _config.SystemMessage = SystemPromptEditor.Text;
        _config.Model = ModelPicker.SelectedItem?.ToString() ?? "gpt-4.1";

        // Find the chat page and update its control
        if (Shell.Current.CurrentPage is ChatPage chatPage)
        {
            // Settings will apply on next session reset
        }

        StatusLabel.Text = "Settings applied! Start a new chat to use them.";
        await Task.Delay(3000);
        StatusLabel.Text = "";
    }
}
