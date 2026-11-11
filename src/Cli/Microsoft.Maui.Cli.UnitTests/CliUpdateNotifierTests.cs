// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Text.Json;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class CliUpdateNotifierTests : IDisposable
{
	readonly string _directory = Path.Combine(AppContext.BaseDirectory, "cli-update-tests", Guid.NewGuid().ToString("N"));
	DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
	string CachePath => Path.Combine(_directory, "cli-update-check.json");

	[Theory]
	[InlineData("1.0.0", "1.0.10")]
	[InlineData("1.0.0-preview.2", "2.0.0-preview.10")]
	[InlineData("1.0.0+installed", "1.0.10")]
	public async Task CheckAsync_NewerVersion_SelectsLatestCompatibleVersion(string current, string expected)
	{
		using var handler = new StubHandler("""{"data":["1.0.0","1.0.9","2.0.0-preview.2","1.0.10+build","2.0.0-preview.10"]}""");
		using var client = new HttpClient(handler);
		var notice = await Create(client).CheckAsync(current, false);

		Assert.Contains($"→ {expected}", notice);
		Assert.DoesNotContain("+", notice);
		Assert.Contains("maui update --self", notice);
		Assert.Equal("https://example.test/autocomplete?id=Microsoft.Maui.Cli&prerelease=true&semVerLevel=2.0.0", handler.LastUri?.AbsoluteUri);
		Assert.Equal(1, handler.IndexCalls);
	}

	[Theory]
	[InlineData("1.0.0", """{"data":["0.9.0","1.0.0+build","2.0.0-preview.1"]}""")]
	[InlineData("1.0.0", """{"data":[]}""")]
	[InlineData("1.0.0-preview.10", """{"data":["1.0.0-preview.2"]}""")]
	public async Task CheckAsync_NoNewerCompatibleVersion_ReturnsNull(string current, string response)
	{
		using var client = new HttpClient(new StubHandler(response));
		Assert.Null(await Create(client).CheckAsync(current, false));
	}

	[Fact]
	public async Task CheckAsync_PreviewInstalled_CanAdvanceToStable()
	{
		using var client = new HttpClient(new StubHandler("""{"data":["1.0.0-preview.10","1.0.0"]}"""));
		Assert.Contains("→ 1.0.0)", await Create(client).CheckAsync("1.0.0-preview.2", false));
	}

	[Fact]
	public async Task CheckAsync_UsesListedAutocompleteVersions_NotFlatContainerVersions()
	{
		using var client = new HttpClient(new StubHandler("""{"data":["1.1.0"],"versions":["99.0.0"]}"""));
		var notice = await Create(client).CheckAsync("1.0.0", false);
		Assert.Contains("→ 1.1.0)", notice);
		Assert.DoesNotContain("99.0.0", notice);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("[]")]
	[InlineData("""{"resources":[]}""")]
	[InlineData("""{"resources":[{"@type":"SearchAutocompleteService","@id":"http://example.test/autocomplete"}]}""")]
	public async Task CheckAsync_InvalidServiceIndex_IsThrottledWithoutRequestingVersions(string index)
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""") { IndexResponse = index };
		using var client = new HttpClient(handler);
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(1, handler.IndexCalls);
		Assert.Equal(0, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_SameUtcDay_UsesPersistentCache()
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""");
		using var client = new HttpClient(handler);
		var first = await Create(client).CheckAsync("1.0.0", false);
		_now = _now.AddHours(11);

		Assert.Equal(first, await Create(client).CheckAsync("1.0.0+build", false));
		Assert.Equal(1, handler.Calls);
		Assert.Equal(1, handler.IndexCalls);
		using var cache = JsonDocument.Parse(File.ReadAllText(CachePath));
		Assert.Equal("1.0.0", cache.RootElement.GetProperty("currentVersion").GetString());
		Assert.Empty(Directory.GetFiles(_directory, "*.pending"));
	}

	[Fact]
	public async Task CheckAsync_NextUtcDay_Rechecks()
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""");
		using var client = new HttpClient(handler);
		await Create(client).CheckAsync("1.0.0", false);
		_now = new DateTimeOffset(2026, 10, 17, 2, 0, 0, TimeSpan.FromHours(2));
		await Create(client).CheckAsync("1.0.0", false);

		Assert.Equal(2, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_InstalledVersionChanges_DiscardsCachedCandidate()
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""");
		using var client = new HttpClient(handler);
		await Create(client).CheckAsync("1.0.0", false);

		Assert.Null(await Create(client).CheckAsync("1.1.0", false));
		Assert.Equal(2, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_ClockMovesBackwards_RechecksInsteadOfTrustingFutureCache()
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""");
		using var client = new HttpClient(handler);
		await Create(client).CheckAsync("1.0.0", false);
		_now = _now.AddHours(-1);
		handler.OnSend = _ => throw new HttpRequestException("Offline");

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(2, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_SuccessfulRefreshWithoutCandidate_ClearsPreviousNotice()
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""");
		using var client = new HttpClient(handler);
		Assert.NotNull(await Create(client).CheckAsync("1.0.0", true));
		_now = _now.AddDays(1);
		handler.OnSend = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent("""{"data":[]}"""),
		});

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(2, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_Offline_PersistsAttemptBeforeRequest()
	{
		using var handler = new StubHandler("{}")
		{
			OnSend = _ =>
			{
				Assert.True(File.Exists(CachePath));
				throw new HttpRequestException("Offline");
			},
		};
		using var client = new HttpClient(handler);

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(1, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_FailedRefresh_ExpiresCachedSuccessAfterSevenDays()
	{
		using var handler = new StubHandler("""{"data":["1.1.0"]}""");
		using var client = new HttpClient(handler);
		var notice = await Create(client).CheckAsync("1.0.0", true);
		Assert.NotNull(notice);
		handler.OnSend = _ => throw new HttpRequestException("Offline");
		_now = _now.AddDays(7);

		Assert.Equal(notice, await Create(client).CheckAsync("1.0.0", true));
		_now = _now.AddSeconds(1);
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(2, handler.Calls);
	}

	[Theory]
	[InlineData("{")]
	[InlineData("{}")]
	[InlineData("[]")]
	[InlineData("""{"currentVersion":42}""")]
	[InlineData("""{"currentVersion":"1.0.0","lastAttemptUtc":{},"latestCandidate":null,"lastSuccessUtc":null}""")]
	[InlineData("""{"currentVersion":"1.0.0","lastAttemptUtc":"invalid","latestCandidate":null,"lastSuccessUtc":null}""")]
	[InlineData("""{"currentVersion":"1.0.0","lastAttemptUtc":"2026-10-16T12:00:00Z","latestCandidate":[],"lastSuccessUtc":null}""")]
	[InlineData("""{"currentVersion":"1.0.0","lastAttemptUtc":"2026-10-16T12:00:00Z","latestCandidate":null,"lastSuccessUtc":{}}""")]
	[InlineData("""{"currentVersion":"1.0.0","lastAttemptUtc":"2026-10-16T12:00:00Z","latestCandidate":null,"lastSuccessUtc":"invalid"}""")]
	public async Task CheckAsync_MalformedCache_TreatsAsStale(string cache)
	{
		Directory.CreateDirectory(_directory);
		File.WriteAllText(CachePath, cache);
		using var client = new HttpClient(new StubHandler("""{"data":["1.1.0"]}"""));

		Assert.NotNull(await Create(client).CheckAsync("1.0.0", true));
	}

	[Theory]
	[InlineData("{")]
	[InlineData("{}")]
	[InlineData("[]")]
	[InlineData("""{"data":[42]}""")]
	public async Task CheckAsync_MalformedFeed_DoesNotThrowOrRetrySameDay(string response)
	{
		using var handler = new StubHandler(response);
		using var client = new HttpClient(handler);

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(1, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_HttpFailure_DoesNotThrow()
	{
		using var handler = new StubHandler("{}") { StatusCode = HttpStatusCode.ServiceUnavailable };
		using var client = new HttpClient(handler);
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CheckAsync_Timeout_CancelsRequestAndDoesNotRetrySameDay(bool slowBody)
	{
		using var handler = new StubHandler("{}")
		{
			OnSend = async token =>
			{
				if (!slowBody)
					await Task.Delay(Timeout.InfiniteTimeSpan, token);
				return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) };
			},
		};
		using var client = new HttpClient(handler);
		var watch = System.Diagnostics.Stopwatch.StartNew();

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(1, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_LockContended_SkipsWithoutRequest()
	{
		Directory.CreateDirectory(_directory);
		using var cacheLock = new FileStream(CachePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		using var handler = new StubHandler("{}");
		using var client = new HttpClient(handler);

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(0, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_UnwritableCache_SkipsWithoutRequest()
	{
		Directory.CreateDirectory(_directory);
		File.WriteAllText(CachePath, "not a directory");
		using var handler = new StubHandler("{}");
		using var client = new HttpClient(handler);
		var notifier = new CliUpdateNotifier(client, () => _now, Path.Combine(CachePath, "cache.json"));

		Assert.Null(await notifier.CheckAsync("1.0.0", false));
		Assert.Equal(0, handler.Calls);
	}

	[Fact]
	public async Task CheckAsync_AttemptCannotBePersisted_RepeatedInvocationsDoNotRequestFeed()
	{
		Directory.CreateDirectory(CachePath);
		using var handler = new StubHandler("{}");
		using var client = new HttpClient(handler);

		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Null(await Create(client).CheckAsync("1.0.0", false));
		Assert.Equal(0, handler.Calls);
		Assert.Empty(Directory.GetFiles(_directory, "*.pending"));
	}

	CliUpdateNotifier Create(HttpClient client) => new(client, () => _now, CachePath);

	public void Dispose()
	{
		if (Directory.Exists(_directory))
			Directory.Delete(_directory, recursive: true);
	}

	sealed class SlowStream : MemoryStream
	{
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return 0;
		}
	}

	sealed class StubHandler(string response) : HttpMessageHandler
	{
		public int Calls { get; private set; }
		public int IndexCalls { get; private set; }
		public string IndexResponse { get; init; } = """{"resources":[{"@type":"SearchAutocompleteService","@id":"https://example.test/autocomplete"}]}""";
		public Uri? LastUri { get; private set; }
		public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
		public Func<CancellationToken, Task<HttpResponseMessage>>? OnSend { get; set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri?.AbsoluteUri == CliUpdateNotifier.FeedUrl)
			{
				IndexCalls++;
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(IndexResponse) });
			}
			Calls++;
			LastUri = request.RequestUri;
			return OnSend?.Invoke(cancellationToken) ?? Task.FromResult(new HttpResponseMessage(StatusCode)
			{
				Content = new StringContent(response),
			});
		}
	}
}
