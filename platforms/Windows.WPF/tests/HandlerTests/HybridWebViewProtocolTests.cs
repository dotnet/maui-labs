using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers.WPF;

namespace HandlerTests;

public class HybridWebViewProtocolTests
{
	[Fact]
	public void RawMessage_IsDecodedAndRaisedThroughIHybridWebView()
	{
		var webView = new HybridWebView();
		var received = new List<string?>();
		webView.RawMessageReceived += (_, e) => received.Add(e.Message);
		const string message = "hello|world 100% é";

		// The script URL-encodes the raw message, then WebView2's sender encodes the whole message.
		var handled = HybridWebViewProtocol.HandleWebMessage(webView,
			Uri.EscapeDataString("__RawMessage|" + Uri.EscapeDataString(message)));

		Assert.True(handled);
		Assert.Equal([message], received);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("no-pipe")]
	[InlineData("__InvokeJavaScriptCompleted%7C1%7Cnull")]
	public void UnsupportedMessages_AreNotDispatched(string? webMessage)
	{
		var webView = new HybridWebView();
		var received = 0;
		webView.RawMessageReceived += (_, _) => received++;

		Assert.False(HybridWebViewProtocol.HandleWebMessage(webView, webMessage));
		Assert.Equal(0, received);
	}

	[Fact]
	public async Task InvokeDotNet_CallsTheTargetThroughTheInvoker()
	{
		var webView = new HybridWebView();
		webView.SetInvokeJavaScriptTarget(new Target());

		var response = await InvokeAsync(webView, """{"MethodName":"Add","ParamValues":["2","3"]}""");

		Assert.False(response.GetProperty("IsError").GetBoolean());
		Assert.True(response.GetProperty("IsJson").GetBoolean());
		Assert.Equal("5", response.GetProperty("Result").GetString());
	}

	[Fact]
	public async Task InvokeDotNet_VoidMethodWithoutParameters_ReturnsNullResult()
	{
		var webView = new HybridWebView();
		var target = new Target();
		webView.SetInvokeJavaScriptTarget(target);

		var response = await InvokeAsync(webView, """{"MethodName":"Ping"}""");

		Assert.False(response.GetProperty("IsError").GetBoolean());
		Assert.False(response.GetProperty("IsJson").GetBoolean());
		Assert.Equal(JsonValueKind.Null, response.GetProperty("Result").ValueKind);
		Assert.Equal(1, target.Pings);
	}

	[Fact]
	public async Task InvokeDotNet_UnknownMethod_ReturnsAnError()
	{
		var webView = new HybridWebView();
		webView.SetInvokeJavaScriptTarget(new Target());

		var response = await InvokeAsync(webView, """{"MethodName":"Missing"}""");

		Assert.True(response.GetProperty("IsError").GetBoolean());
		Assert.False(string.IsNullOrEmpty(response.GetProperty("ErrorMessage").GetString()));
	}

	[Fact]
	public async Task InvokeDotNet_WithoutMethodName_ReturnsAnError()
	{
		var webView = new HybridWebView();
		webView.SetInvokeJavaScriptTarget(new Target());

		var response = await InvokeAsync(webView, """{"ParamValues":[]}""");

		Assert.True(response.GetProperty("IsError").GetBoolean());
	}

	static async Task<JsonElement> InvokeAsync(IHybridWebView webView, string body)
	{
		var bytes = await HybridWebViewProtocol.InvokeDotNetAsync(webView, body);
		using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
		return doc.RootElement.Clone();
	}

	public class Target
	{
		public int Pings { get; private set; }

		public int Add(int a, int b) => a + b;

		public void Ping() => Pings++;
	}
}
