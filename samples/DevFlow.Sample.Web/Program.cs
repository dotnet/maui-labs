using System.Runtime.InteropServices.JavaScript;
using Microsoft.Maui.Platform.Web;
using Microsoft.Maui.Platform.Web.Hosting;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Web;
#endif

namespace DevFlow.Sample.Web;

public class SampleWebApplication : WebMauiApplication
{
	// Integration tests choose the agent port with ?devflowPort=<port>, as other samples read
	// DEVFLOW_TEST_PORT; a page has no environment variables.
	static int? ResolveAgentPort()
	{
		var query = JSHost.GlobalThis.GetPropertyAsJSObject("location")?.GetPropertyAsString("search") ?? "";
		return query.TrimStart('?').Split('&')
			.Select(pair => pair.Split('=', 2))
			.Where(pair => pair.Length == 2 && pair[0] == "devflowPort" && int.TryParse(pair[1], out _))
			.Select(pair => (int?)int.Parse(pair[1]))
			.FirstOrDefault();
	}

	protected override MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiAppWeb<App>();

#if DEBUG
		builder.AddMauiDevFlowAgent(options =>
		{
			if (ResolveAgentPort() is { } port)
				options.Port = port;
		});
#endif

		return builder.Build();
	}
}

public static class Program
{
	public static Task Main() => new SampleWebApplication().RunAsync();
}
