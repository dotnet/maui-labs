using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class ToolRenderingPage : ContentPage
{
    public AgentContext Session { get; }

    public ToolRenderingPage(IChatClient chatClient)
    {
        var tools = new SampleTools().GetTools();
        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a helpful assistant with access to tools.
                When asked about the weather, use the GetCurrentWeather tool.
                When asked to calculate something, use the calculate tool.
                Always explain what you found after using a tool.
                """,
            Tools = [.. tools]
        };
        var agent = new UIAgent(chatClient, options =>
        {
            options.ChatOptions = chatOptions;
            // Register source-generated tool block handlers (e.g. WeatherToolBlock)
            options.AddGeneratedToolBlocks();
        });
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
    }
}
