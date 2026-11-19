using System.Text.Json;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class WebViewCliTargetingTests
{
    private const string Selector = "[data-name=\"a'b\\\\c\"]";
    private const string Text = "Hello \"quoted\"\n'world'\\";

    [Theory]
    [InlineData("DOM", "querySelector")]
    [InlineData("DOM", "querySelectorAll")]
    [InlineData("DOM", "getOuterHTML")]
    [InlineData(null, "snapshot")]
    [InlineData("Input", "dispatchClickEvent")]
    [InlineData("Input", "fill")]
    public async Task Helpers_SelectedTargets_ForwardTargetOutsideJavaScript(string? domain, string command)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);

        foreach (var (alias, option, target) in new[]
        {
            ("webview", "--webview", "Left"),
            ("cdp", "-w", "Right"),
            ("webview", "-w", "1"),
            ("webview", "--webview", "el-right & pane")
        })
        {
            server.ClearRecordedRequests();
            var result = await cli.InvokeAsync(BuildArguments(alias, domain, command, option, target));

            Assert.Equal(0, result.ExitCode);
            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
            Assert.Equal($"?webview={Uri.EscapeDataString(target)}", request.QueryString);
            AssertExpression(request, command);
            Assert.DoesNotContain(server.RecordedRequests, r => r.Path.StartsWith("/api/v1/webview/input/", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("DOM", "querySelector")]
    [InlineData("DOM", "querySelectorAll")]
    [InlineData("DOM", "getOuterHTML")]
    [InlineData(null, "snapshot")]
    [InlineData("Input", "dispatchClickEvent")]
    [InlineData("Input", "fill")]
    public async Task Helpers_NoTarget_PreserveDefaultSelection(string? domain, string command)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);

        var result = await cli.InvokeAsync(BuildArguments("webview", domain, command));

        Assert.Equal(0, result.ExitCode);
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal(string.Empty, request.QueryString);
        AssertExpression(request, command);
    }

    [Theory]
    [InlineData("webview", "--webview", "Left")]
    [InlineData("cdp", "-w", "Right")]
    [InlineData("webview", null, null)]
    public async Task InsertText_PreservesCdpMethodAndTarget(string alias, string? option, string? target)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var arguments = new List<string> { "devflow", alias, "Input", "insertText", Text };
        if (option is not null)
            arguments.AddRange([option, target!]);

        var result = await cli.InvokeAsync(arguments.ToArray());

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"Inserted: {Text.Length} characters", result.StdOut);
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal(target is null ? string.Empty : $"?webview={Uri.EscapeDataString(target)}", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Input.insertText", body.RootElement.GetProperty("method").GetString());
        Assert.Equal(Text, body.RootElement.GetProperty("params").GetProperty("text").GetString());
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path.StartsWith("/api/v1/webview/input/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commands_InputClick_AdvertisesRegisteredCommand()
    {
        var cli = new CliTestHarness(0);

        var result = await cli.InvokeRawAsync("devflow", "commands", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("webview Input dispatchClickEvent", result.StdOut);
        Assert.DoesNotContain("webview Input click", result.StdOut);
    }

    private static string[] BuildArguments(string alias, string? domain, string command, string? option = null, string? target = null)
    {
        var arguments = new List<string> { "devflow", alias };
        if (domain is not null)
            arguments.Add(domain);
        arguments.Add(command);
        if (command != "snapshot")
            arguments.Add(Selector);
        if (command == "fill")
            arguments.Add(Text);
        if (option is not null)
            arguments.AddRange([option, target!]);
        return arguments.ToArray();
    }

    private static void AssertExpression(RecordedRequest request, string command)
    {
        Assert.Equal("POST", request.Method);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Runtime.evaluate", body.RootElement.GetProperty("method").GetString());
        var parameters = body.RootElement.GetProperty("params");
        Assert.True(parameters.GetProperty("returnByValue").GetBoolean());
        var expression = parameters.GetProperty("expression").GetString()!;
        Assert.DoesNotContain("webview", expression);

        if (command == "snapshot")
        {
            Assert.Contains("'  '.repeat(depth)", expression);
            Assert.Contains("walk(document.body, 0)", expression);
            return;
        }

        var queryMethod = command == "querySelectorAll" ? "querySelectorAll" : "querySelector";
        Assert.Contains($"document.{queryMethod}({JsonSerializer.Serialize(Selector)})", expression);
        if (command == "dispatchClickEvent")
        {
            Assert.Equal(1, expression.Split("el.click();", StringSplitOptions.None).Length - 1);
            Assert.Contains("return 'Clicked: '", expression);
            Assert.DoesNotContain("JSON.stringify", expression);
        }
        if (command == "fill")
        {
            Assert.Contains($"const text = {JsonSerializer.Serialize(Text)};", expression);
            Assert.Equal(1, expression.Split("el.value = text;", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, expression.Split("el.dispatchEvent(new Event('input'", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, expression.Split("el.dispatchEvent(new Event('change'", StringSplitOptions.None).Length - 1);
            Assert.Contains("if (el.isContentEditable)", expression);
            Assert.Contains("return 'Filled: '", expression);
            Assert.DoesNotContain("JSON.stringify", expression);
        }
    }
}
