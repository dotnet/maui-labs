using System.ComponentModel;
using System.Text.Json;
using Microsoft.Maui.AI.Chat;
using Microsoft.Extensions.AI;

namespace AiControlsSample;

public partial class ToolRenderingPage : ContentPage
{
    public AgentContext Session { get; }

    public ToolRenderingPage(IChatClient chatClient)
    {
        var tools = new List<AITool>(new SampleTools().GetTools());

        // Approval demo: create_plan must be approved by the user before it runs.
        tools.Add(new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create(
                [Description("Create a plan with numbered steps for the user to review and approve before execution.")]
                ([Description("JSON array of step descriptions")] string steps_json) =>
                {
                    var steps = JsonSerializer.Deserialize<List<string>>(steps_json) ?? [];
                    var formatted = string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"));
                    return $"Plan approved and executing:\n{formatted}";
                },
                "create_plan")));

        // Media demo: lets the chat model generate images inline (via UseImageGeneration).
        tools.Add(new HostedImageGenerationTool());

        var chatOptions = new ChatOptions
        {
            Instructions = """
                You are a helpful assistant that demonstrates the core chat features.
                - For weather, call GetCurrentWeather.
                - For math, call the calculate tool.
                - To make a step-by-step plan, call create_plan with a JSON array of steps.
                  The user must approve it before it runs. If rejected, ask what to change
                  and do NOT repeat the plan.
                - To draw or generate a picture, use image generation.
                - To test error handling, call TriggerError.
                - Format text answers with **bold** for emphasis and "- " bullets for lists.
                """,
            Tools = [.. tools]
        };

        var agent = new UIAgent(chatClient, options =>
        {
            options.ChatOptions = chatOptions;
            // Map raw M.E.AI weather content into a strongly-typed WeatherToolBlock.
            options.AddBlockHandler(new WeatherToolBlockHandler());
            // Parse assistant text into a formatted rich-text block (sample pattern).
            options.AddBlockHandler(new FormattedTextHandler());
        });
        Session = new AgentContext(agent);

        InitializeComponent();
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Session.Clear();
    }
}
