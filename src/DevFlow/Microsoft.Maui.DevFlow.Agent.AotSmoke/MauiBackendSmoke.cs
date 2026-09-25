#if MAUI_BACKEND_SMOKE
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

static class MauiBackendSmoke
{
    public static async Task RunAsync()
    {
        var button = new Button { AutomationId = "maui-button", Text = "Initial", Shadow = new Shadow { Radius = 12 } };
        var label = new Label { AutomationId = "maui-label", Text = "Label", FontAttributes = FontAttributes.Bold, Opacity = 0.5 };
        button.Clicked += (_, _) => button.Text = "Clicked";
        var custom = new CustomView { AutomationId = "maui-custom", Message = "Custom" };
        MauiAgentInspection.RegisterType<CustomView>();
        using var agent = new Backend(button, label, custom);
        await agent.AssertTreeAsync();
        await agent.AssertPropertyAsync("maui-label", "Opacity", "0.5");
        await agent.AssertPropertyAsync("maui-label", "FontAttributes", "Bold");
        await agent.AssertPropertyAsync("maui-custom", "Message", "Custom");
        await agent.AssertPropertyAsync("maui-button", "Shadow.Radius", "12");
        await agent.AssertTapAsync();
        await agent.AssertPropertyAsync("maui-button", "Text", "Clicked");
        await agent.AssertSetPropertyAsync();
        await agent.AssertPropertyAsync("maui-button", "Text", "Updated");
        await agent.AssertDescriptorsAsync();
        await agent.AssertThemeAsync();
        _ = new HttpRequest { Body = "{}" }.BodyAs<LayoutInspectionRequest>();
        _ = HttpResponse.Json(new LayoutInspectionResult());
        using var sensors = new SensorManager();
        using var sensorJson = JsonDocument.Parse(HttpResponse.Json(sensors.GetStatus()).Body!);
        if (sensorJson.RootElement.GetArrayLength() != 6)
            throw new InvalidOperationException("Sensor status JSON did not include all sensor contracts.");
        Console.WriteLine("PASS: real MAUI Core tree, tap, property read/write, descriptors, custom metadata, and layout JSON (headless; no native rendering).");
    }

    private sealed class CustomView : ContentView
    {
        public string Message { get; set; } = "";
    }

    private sealed class TestApplication(params View[] children) : Application, IVisualTreeElement
    {
        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => children;
        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }

    private sealed class Backend : MauiDevFlowAgentService
    {
        public Backend(params View[] children) : base(new AgentOptions { RequireMutationLease = false })
        {
            _app = new TestApplication(children);
            _dispatcher = new DelegateAgentDispatcher(() => false, action => action());
        }

        public async Task AssertTreeAsync()
        {
            var response = await HandleTree(new HttpRequest());
            Check(response);
            if (!response.Body!.Contains("maui-button", StringComparison.Ordinal))
                throw new InvalidOperationException("MAUI tree omitted the button.");
        }

        public async Task AssertPropertyAsync(string id, string name, string expected)
        {
            var response = await HandleProperty(new HttpRequest { RouteParams = { ["id"] = id, ["name"] = name } });
            Check(response);
            using var document = JsonDocument.Parse(response.Body!);
            if (document.RootElement.GetProperty("value").GetString() != expected)
                throw new InvalidOperationException($"Property {id}.{name}: {response.Body}");
        }

        public async Task AssertTapAsync()
            => Check(await HandleTap(new HttpRequest { Body = """{"elementId":"maui-button"}""" }));

        public async Task AssertSetPropertyAsync()
            => Check(await HandleSetProperty(new HttpRequest
            {
                RouteParams = { ["id"] = "maui-button", ["name"] = "Text" }, Body = """{"value":"Updated"}"""
            }));

        public async Task AssertDescriptorsAsync()
        {
            var response = await HandlePropertyDescriptors(new HttpRequest { RouteParams = { ["id"] = "maui-label" } });
            Check(response);
            using var document = JsonDocument.Parse(response.Body!);
            var attributes = document.RootElement.GetProperty("properties").EnumerateArray()
                .Single(property => property.GetProperty("name").GetString() == "FontAttributes");
            if (!attributes.GetProperty("choices").EnumerateArray().Any(value => value.GetString() == "Bold"))
                throw new InvalidOperationException("Enum property choices were trimmed.");
        }

        public async Task AssertThemeAsync()
        {
            var response = await HandleThemeGet(new HttpRequest());
            Check(response);
            using var document = JsonDocument.Parse(response.Body!);
            if (document.RootElement.GetProperty("supportedThemes").GetArrayLength() != 3
                || document.RootElement.GetProperty("message").ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("Theme response metadata or null policy changed.");
        }

        private static void Check(HttpResponse response)
        {
            if (response.StatusCode != 200)
                throw new InvalidOperationException(response.Body);
        }
    }
}
#endif
