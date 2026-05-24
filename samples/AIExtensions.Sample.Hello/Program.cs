using System.ClientModel;
using System.ComponentModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

// Smallest possible end-to-end example of Microsoft.Maui.AI.Attributes:
//   - WeatherService  -> instance method, resolved from DI
//   - GreetingService  -> pure static method, no DI needed
// The source generator auto-discovers all [ExportAIFunction] methods in the
// assembly and creates AIExtensionsSampleHelloToolContext with every tool.

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var apiKey = configuration["AI:ApiKey"];
var endpoint = configuration["AI:Endpoint"];
var deployment = configuration["AI:DeploymentName"];

if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(deployment))
{
    Console.Error.WriteLine("""
        AI:Endpoint, AI:ApiKey and AI:DeploymentName must be set. Configure user-secrets:

          dotnet user-secrets --id ai-attributes-secrets set "AI:Endpoint" "<endpoint>"
          dotnet user-secrets --id ai-attributes-secrets set "AI:ApiKey" "<key>"
          dotnet user-secrets --id ai-attributes-secrets set "AI:DeploymentName" "<deployment>"

        (shared across all AI.Attributes samples)
        """);
    return 1;
}

var services = new ServiceCollection();
services.AddSingleton<WeatherService>();

var azure = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
IChatClient innerClient = azure.GetChatClient(deployment).AsIChatClient();
services.AddSingleton(innerClient);

var root = services.BuildServiceProvider();

var chat = new ChatClientBuilder(root.GetRequiredService<IChatClient>())
    .UseFunctionInvocation()
    .Build(root);

var tools = AIExtensionsSampleHelloToolContext.Default.Tools;
var options = new ChatOptions { Tools = [.. tools] };

Console.WriteLine($"{tools.Count} tool(s) registered:");
foreach (var t in tools)
    Console.WriteLine($"  - {t.Name}: {t.Description}");
Console.WriteLine();
Console.WriteLine("Try: \"What's the forecast in Tokyo?\" or \"Say hello to Ada.\"");
Console.WriteLine("Ctrl+C to exit.");
Console.WriteLine();

var history = new List<ChatMessage>
{
    new(ChatRole.System, "You are a helpful assistant. Use the available tools when relevant.")
};

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
        continue;

    history.Add(new ChatMessage(ChatRole.User, input));

    var response = await chat.GetResponseAsync(history, options);
    history.AddMessages(response);

    Console.WriteLine(response.Text);
    Console.WriteLine();
}

/// <summary>
/// Instance service resolved from DI. The generated tool calls
/// <c>provider.GetRequiredService&lt;WeatherService&gt;()</c> to get this instance.
/// </summary>
public class WeatherService
{
    [Description("Gets a short weather forecast for a city.")]
    [ExportAIFunction("get_forecast")]
    public string GetForecast(
        [Description("The city name")] string city,
        [Description("Number of days (1-7). Defaults to 3.")] int days = 3)
    {
        return $"{days}-day forecast for {city}: mostly pleasant with a high of {Random.Shared.Next(15, 35)}°C.";
    }
}

/// <summary>
/// Pure-static service — no instance, no DI. The generator calls these methods
/// directly without touching <see cref="IServiceProvider"/>.
/// </summary>
public static class GreetingService
{
    [Description("Greets someone by name and gives them a lucky number.")]
    [ExportAIFunction("say_hello")]
    public static string SayHello(
        [Description("The name of the person to greet")] string name)
        => $"Hello, {name}! Your lucky number is {Random.Shared.Next(1, 100)}.";
}
