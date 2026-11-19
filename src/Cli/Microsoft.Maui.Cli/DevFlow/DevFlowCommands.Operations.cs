using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.Cli.DevFlow;

public partial class DevFlowCommands
{
    internal static Option<string[]> HostPluginOption { get; private set; } = null!;

    private static Command CreateOperationsCommand(Option<string> hostOption, Option<int> portOption,
        Option<string?> deviceOption, Option<bool> jsonOption, Option<bool> noJsonOption)
    {
        var command = new Command("operations", "Discover and invoke app, built-in, or trusted local host operations");
        var location = new Option<string>("--location") { Description = "Discovery location: agent, host, or all", DefaultValueFactory = _ => "agent", Recursive = true };
        command.Add(location);
        foreach (var verb in new[] { "list", "describe", "call", "events", "watch" })
        {
            var child = new Command(verb, $"{verb} operations using the shared catalog");
            var provider = new Argument<string>("provider") { Description = "Provider identity, e.g. devflow.actions or host:com.example.adb" };
            var name = new Argument<string>("name") { Description = "Operation or stream name" };
            var parameters = new Argument<string?>("parameters") { Description = "JSON object parameters", DefaultValueFactory = _ => null };
            var since = new Option<long?>("--since") { Description = "Read events after this cursor; watch starts live unless supplied" };
            var limit = new Option<int>("--limit") { DefaultValueFactory = _ => 100, Description = "Maximum events per page (1-1000)" };
            if (verb != "list") child.Add(provider);
            if (verb is "call" or "events" or "watch") child.Add(name);
            if (verb == "call") child.Add(parameters);
            if (verb is "events" or "watch") { child.Add(since); child.Add(limit); }
            child.SetAction(async (parse, ct) =>
            {
                var json = Output.ResolveJsonMode(parse.GetValue(jsonOption), parse.GetValue(noJsonOption));
                var hosts = DevFlowHostPlugins.Current;
                Task<AgentClient> Agent() => CreateAgentClientAsync(parse.GetValue(hostOption)!, parse.GetValue(portOption));
                if (verb == "list")
                {
                    var selected = parse.GetValue(location);
                    if (selected is not ("agent" or "host" or "all"))
                        throw new ArgumentException("--location must be agent, host, or all.");
                    var providers = selected == "agent" ? new Dictionary<string, ExtensionDescriptor>() : await hosts.DescribeAsync(ct);
                    if (selected != "host")
                    {
                        using var agent = await Agent();
                        foreach (var pair in await agent.GetOperationProvidersAsync())
                        {
                            if (pair.Key.StartsWith("host:", StringComparison.Ordinal))
                                throw new InvalidOperationException("The app advertised a reserved host provider identity.");
                            providers.Add(pair.Key, pair.Value);
                        }
                    }
                    Output.WriteResult(providers, json, _ =>
                    {
                        foreach (var pair in providers.OrderBy(p => p.Key, StringComparer.Ordinal))
                            Console.WriteLine($"{pair.Key}  {pair.Value.State ?? "ready"}  {pair.Value.Description}");
                    });
                    return;
                }

                var key = parse.GetValue(provider)!;
                var hostSide = key.StartsWith("host:", StringComparison.Ordinal);
                var local = hostSide ? await hosts.DescribeAsync(ct) : null;
                using var client = !hostSide ? await Agent() : null;
                var descriptors = local ?? await client!.GetOperationProvidersAsync();
                if (!descriptors.TryGetValue(key, out var descriptor))
                    throw new KeyNotFoundException($"Operation provider '{key}' was not found.");
                if (verb == "describe")
                {
                    Output.WriteResult(descriptor, json, d =>
                    {
                        Console.WriteLine($"{key}: {d.Description}");
                        foreach (var tool in d.Tools) Console.WriteLine($"  {tool.Name}: {tool.Description} (target: {tool.Target})");
                        foreach (var stream in d.Streams) Console.WriteLine($"  stream {stream.Name}: {stream.Description}");
                    });
                    return;
                }
                var operationName = parse.GetValue(name)!;
                if (verb == "call")
                {
                    var tool = descriptor.Tools.FirstOrDefault(t => t.Name == operationName)
                        ?? throw new KeyNotFoundException($"Operation '{key}/{operationName}' was not found.");
                    JsonElement? args = parse.GetValue(parameters) is { } text ? CliJson.ParseElement(text) : null;
                    if (!hostSide)
                    {
                        if (!hosts.ProviderPolicy.AllowsPath(key, tool.Path))
                            throw new InvalidOperationException($"Route '{tool.Path}' is outside the bindings approved for '{key}'.");
                        Console.WriteLine(await client!.CallExtensionToolAsync(tool.Method, tool.Path, args));
                        return;
                    }
                    using var targetClient = tool.Target == "agent" ? await Agent() : null;
                    var uri = targetClient is null ? null : new Uri(targetClient.BaseUrl);
                    var response = await hosts.InvokeAsync(key, operationName, args,
                        new(parse.GetValue(deviceOption), uri?.Host, uri?.Port), targetClient, ct);
                    if (response.StatusCode >= 400)
                        throw new InvalidOperationException(response.Body ?? $"Host operation returned HTTP {response.StatusCode}.");
                    Console.WriteLine(response.Body ?? Convert.ToBase64String(response.BodyBytes ?? []));
                    return;
                }

                var stream = descriptor.Streams.FirstOrDefault(s => s.Name == operationName)
                    ?? throw new KeyNotFoundException($"Stream '{key}/{operationName}' was not found.");
                if (!hostSide && verb == "watch")
                {
                    await new ExtensionOperations(client!).WatchAsync(key, operationName, parse.GetValue(since),
                        item => Console.WriteLine(JsonSerializer.Serialize(item, DevFlowCliJsonContext.Default.ExtensionStreamEvent)), ct);
                    return;
                }
                long? cursor = parse.GetValue(since) ?? (verb == "events" ? 0 : null);
                var maximum = Math.Clamp(parse.GetValue(limit), 1, 1000);
                async Task<ExtensionStreamPage> ReadAsync()
                {
                    if (!hostSide)
                    {
                        if (!hosts.ProviderPolicy.AllowsPath(key, stream.Path))
                            throw new InvalidOperationException($"Stream '{stream.Path}' is outside the bindings approved for '{key}'.");
                        return await client!.GetExtensionEventsAsync(stream, cursor ?? 0, maximum);
                    }
                    var response = await hosts.ReadStreamAsync(key, operationName, cursor, maximum, ct);
                    if (response.StatusCode >= 400)
                        throw new InvalidOperationException(response.Body);
                    return JsonSerializer.Deserialize(response.Body!, DevFlowCliJsonContext.Default.ExtensionStreamPage)
                        ?? throw new JsonException("Host stream returned a null page.");
                }
                var page = await ReadAsync();
                if (verb == "events")
                {
                    Output.WriteResult(page, json);
                    return;
                }
                while (!ct.IsCancellationRequested)
                {
                    if (page.Dropped > 0) Console.Error.WriteLine($"Stream dropped {page.Dropped} events.");
                    foreach (var item in page.Events)
                        Console.WriteLine(JsonSerializer.Serialize(item, DevFlowCliJsonContext.Default.ExtensionStreamEvent));
                    cursor = page.NextSequence;
                    await Task.Delay(250, ct);
                    page = await ReadAsync();
                }
            });
            command.Add(child);
        }
        return command;
    }
}
