#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Comet.DevTools;
using Xunit;

namespace Comet.Tests;

public class CometDevAgentScreenshotTests
{
	[Theory]
	[InlineData("/screenshot")]
	[InlineData("/api/v1/ui/screenshot?fullscreen=true")]
	public async Task Screenshot_AsyncProvider_LeavesUiThreadFreeForCaptureCallback(string path)
	{
		using var host = new ScreenshotHost();
		var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
		byte[] payload = [1, 2, 3];
		CometDevRegistry.ScreenshotProvider = () =>
			throw new InvalidOperationException("The legacy provider must not replace an async capture.");
		CometDevRegistry.ScreenshotProviderAsync = () =>
		{
			Assert.Equal(host.UiThreadId, Environment.CurrentManagedThreadId);
			host.Dispatch(() => completion.SetResult(payload));
			return completion.Task;
		};

		using var response = await host.Client.GetAsync(path);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
		Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
	}

	[Theory]
	[InlineData("/screenshot")]
	[InlineData("/api/v1/ui/screenshot")]
	public async Task Screenshot_LegacyProvider_PreservesUiThreadDispatchAndBytes(string path)
	{
		using var host = new ScreenshotHost();
		byte[] payload = [4, 5, 6];
		CometDevRegistry.ScreenshotProvider = () =>
		{
			Assert.Equal(host.UiThreadId, Environment.CurrentManagedThreadId);
			return payload;
		};

		using var response = await host.Client.GetAsync(path);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Screenshot_CaptureFailure_ReturnsErrorAndKeepsAgentResponsive(bool asynchronous)
	{
		using var host = new ScreenshotHost();
		const string error = "Window PixelCopy failed with result 3.";
		if (asynchronous)
			CometDevRegistry.ScreenshotProviderAsync = () =>
				Task.FromException<byte[]?>(new InvalidOperationException(error));
		else
			CometDevRegistry.ScreenshotProvider = () => throw new InvalidOperationException(error);

		using var response = await host.Client.GetAsync("/screenshot");
		using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.False(body.RootElement.GetProperty("ok").GetBoolean());
		Assert.Equal(error, body.RootElement.GetProperty("error").GetString());
		using var status = await host.Client.GetAsync("/status");
		Assert.Equal(HttpStatusCode.OK, status.StatusCode);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Screenshot_NoImage_ReturnsServiceUnavailable(bool providerRegistered)
	{
		using var host = new ScreenshotHost();
		if (providerRegistered)
			CometDevRegistry.ScreenshotProviderAsync = () => Task.FromResult<byte[]?>([]);

		using var response = await host.Client.GetAsync("/screenshot");

		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.NotEqual("image/png", response.Content.Headers.ContentType?.MediaType);
	}

	sealed class ScreenshotHost : IDisposable
	{
		readonly BlockingCollection<Action> _queue = new();
		readonly Thread _uiThread;
		readonly CometDevAgent _agent;
		readonly Func<byte[]?>? _previousProvider = CometDevRegistry.ScreenshotProvider;
		readonly Func<Task<byte[]?>>? _previousAsyncProvider = CometDevRegistry.ScreenshotProviderAsync;
		readonly Action<Action>? _previousEnqueue = CometDevRegistry.MainThreadEnqueue;
		readonly bool _previousEnabled = CometDevRegistry.Enabled;

		public HttpClient Client { get; }
		public int UiThreadId => _uiThread.ManagedThreadId;
		public void Dispatch(Action action) => _queue.Add(action);

		public ScreenshotHost()
		{
			CometDevRegistry.ScreenshotProvider = null;
			CometDevRegistry.ScreenshotProviderAsync = null;
			_uiThread = new Thread(() =>
			{
				foreach (var action in _queue.GetConsumingEnumerable())
					action();
			}) { IsBackground = true };
			_uiThread.Start();

			var portProbe = new TcpListener(IPAddress.Loopback, 0);
			portProbe.Start();
			var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
			portProbe.Stop();
			_agent = new CometDevAgent(port, Dispatch);
			_agent.Start();
			Client = new HttpClient
			{
				BaseAddress = new Uri($"http://127.0.0.1:{_agent.Port}"),
				Timeout = TimeSpan.FromSeconds(5),
			};
		}

		public void Dispose()
		{
			Client.Dispose();
			_agent.Stop();
			_queue.CompleteAdding();
			var stopped = _uiThread.Join(TimeSpan.FromSeconds(5));
			CometDevRegistry.ScreenshotProvider = _previousProvider;
			CometDevRegistry.ScreenshotProviderAsync = _previousAsyncProvider;
			CometDevRegistry.MainThreadEnqueue = _previousEnqueue;
			CometDevRegistry.Enabled = _previousEnabled;
			Assert.True(stopped, "The capture callback must not block the UI dispatcher.");
			_queue.Dispose();
		}
	}
}
