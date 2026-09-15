using System.Text.Json.Serialization;

namespace AIChat.ClientServer.Sample.Shared;

public static class ScenarioIds
{
    public const string AgenticChat = "agentic_chat";
    public const string BackendToolRendering = "backend_tool_rendering";
    public const string FrontendTools = "frontend_tools";
    public const string HumanInTheLoop = "human_in_the_loop";
    public const string ToolBasedGenerativeUi = "tool_based_generative_ui";
    public const string AgenticGenerativeUi = "agentic_generative_ui";
    public const string SharedState = "shared_state";
    public const string PredictiveState = "predictive_state";
    public const string Reasoning = "reasoning";
    public const string Workflow = "workflow";
    public const string SelectiveApproval = "selective_approval";

    public static readonly IReadOnlyList<ScenarioDescriptor> All =
    [
        new(AgenticChat, "Streaming chat"),
        new(BackendToolRendering, "Backend weather tool"),
        new(FrontendTools, "Frontend client tools"),
        new(HumanInTheLoop, "Meeting approval"),
        new(ToolBasedGenerativeUi, "Client generative UI"),
        new(AgenticGenerativeUi, "Agentic plan UI"),
        new(SharedState, "Shared recipe state"),
        new(PredictiveState, "Predictive document state"),
        new(Reasoning, "Reasoning summary"),
        new(Workflow, "Research and reporting workflow"),
        new(SelectiveApproval, "Selective tool approval")
    ];
}

public sealed record ScenarioDescriptor(string Id, string Title);

public sealed record ClientToolDeclaration(string Name, string Description);

public static class ClientToolDeclarations
{
    public static readonly IReadOnlyList<ClientToolDeclaration> All =
    [
        new("show_weather_card", "Render a weather card in the client."),
        new("propose_document", "Present a document proposal for review."),
        new("render_plan", "Render plan progress in the client.")
    ];
}

public sealed class WeatherInfo
{
    [JsonPropertyName("temperature")]
    public int Temperature { get; init; }

    [JsonPropertyName("conditions")]
    public string Conditions { get; init; } = string.Empty;

    [JsonPropertyName("humidity")]
    public int Humidity { get; init; }

    [JsonPropertyName("wind_speed")]
    public int WindSpeed { get; init; }
}

public sealed class Plan
{
    [JsonPropertyName("steps")]
    public List<PlanStep> Steps { get; set; } = [];
}

public sealed class PlanStep
{
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("status")]
    public PlanStepStatus Status { get; set; } = PlanStepStatus.Pending;
}

[JsonConverter(typeof(JsonStringEnumConverter<PlanStepStatus>))]
public enum PlanStepStatus { Pending, Completed }

public sealed class JsonPatchOperation
{
    [JsonPropertyName("op")]
    public required string Op { get; set; }

    [JsonPropertyName("path")]
    public required string Path { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

public sealed class Recipe
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("ingredients")]
    public List<Ingredient> Ingredients { get; set; } = [];

    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; set; } = [];
}

public sealed class Ingredient
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;
}

public sealed class RecipeResponse
{
    [JsonPropertyName("recipe")]
    public Recipe Recipe { get; set; } = new();
}

public sealed class DocumentState
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class ClientAgentState
{
    [JsonPropertyName("plan")]
    public Plan Plan { get; set; } = new();

    [JsonPropertyName("recipe")]
    public Recipe? Recipe { get; set; }

    [JsonPropertyName("document")]
    public DocumentState Document { get; set; } = new();
}

public sealed class DocumentProposal
{
    [JsonPropertyName("document")]
    public DocumentState Document { get; set; } = new();

    [JsonPropertyName("accepted")]
    public bool? Accepted { get; set; }
}

public sealed class DocumentProposalSnapshot
{
    [JsonPropertyName("proposal")]
    public DocumentProposal Proposal { get; set; } = new();
}
