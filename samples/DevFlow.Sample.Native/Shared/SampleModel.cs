using System.Text.Json;

namespace DevFlow.Sample.Native;

/// <summary>
/// A single todo row. Mirrors <c>samples/DevFlow.Sample/TodoItem.cs</c> so the same
/// integration assertions work against both the MAUI and the native samples.
/// </summary>
public sealed class SampleTodo(string title, string description)
{
    public string Title { get; set; } = title;
    public string Description { get; set; } = description;
    public bool IsDone { get; set; }
}

/// <summary>
/// The framework-neutral behaviour behind the native samples.
///
/// Every platform head renders the same logical screen with the same accessibility
/// identifiers, so <c>Microsoft.Maui.DevFlow.Agent.IntegrationTests</c> can point at any of
/// them without per-platform assertions. Keeping the behaviour here means the heads only
/// contain view construction.
/// </summary>
public sealed class SampleModel
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Matches the MAUI sample's HeaderLabel verbatim. The shared integration tests assert on this
    /// text (Query_ByText, Query_ByAutomationId_HasCorrectProperties, GetProperty_Text), so the two
    /// samples have to agree — same accessibility ids *and* same content.
    /// </summary>
    public const string HeaderText = "📝 My Todos";

    public List<SampleTodo> Todos { get; } =
    [
        new("Buy milk", "Two percent"),
        new("Walk the dog", "Around the block"),
        new("Ship DevFlow", "Native agent support"),
    ];

    /// <summary>Text shown in <c>StatusLabel</c>; every interaction updates it.</summary>
    public string Status { get; private set; } = "last action: none";

    /// <summary>Text shown in <c>CountLabel</c>.</summary>
    public string CountText => $"{Todos.Count} items";

    public void Add(string title, string description)
    {
        title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();
        Todos.Add(new SampleTodo(title, description?.Trim() ?? string.Empty));
        Status = $"last action: added {title}";
    }

    public void Remove(SampleTodo todo)
    {
        Todos.Remove(todo);
        Status = $"last action: removed {todo.Title}";
    }

    public void Toggle(SampleTodo todo)
    {
        todo.IsDone = !todo.IsDone;
        Status = $"last action: toggled {todo.Title}";
    }

    public void RecordAction(string action) => Status = $"last action: {action}";

    /// <summary>
    /// Issues a real outbound request so the agent's network capture has something to record.
    /// </summary>
    public async Task<string> FetchPostsAsync()
    {
        try
        {
            var body = await Http.GetStringAsync("https://jsonplaceholder.typicode.com/posts?_limit=3");
            var count = JsonDocument.Parse(body).RootElement.GetArrayLength();
            Status = $"last action: fetched {count} posts";
        }
        catch (Exception ex)
        {
            Status = $"last action: fetch failed ({ex.GetType().Name})";
        }

        return Status;
    }
}
