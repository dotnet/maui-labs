using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Base class for chat client usage tests.
/// Provides common consistency checks for any <see cref="IChatClient"/> implementation.
/// </summary>
/// <param name="output">Receives the provider's actual usage and telemetry measurements.</param>
/// <typeparam name="T">The concrete chat client type to test.</typeparam>
public abstract class ChatClientUsageTestsBase<T>(ITestOutputHelper output)
	where T : class, IChatClient, new()
{
	/// <summary>
	/// Gets whether the provider is expected to report usage in the current environment.
	/// </summary>
	protected virtual bool IsUsageAvailable => true;

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_ReturnsExpectedUsage()
	{
		var response = await GetResponseWithTelemetryAsync(streaming: false);

		AssertExpectedUsage(response.Usage);
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetStreamingResponseAsync_ReturnsExpectedUsage()
	{
		var response = await GetResponseWithTelemetryAsync(streaming: true);

		AssertExpectedUsage(response.Usage);
	}

	private async Task<ChatResponse> GetResponseWithTelemetryAsync(bool streaming)
	{
		var sourceName = $"ChatClientUsageTests.{typeof(T).Name}.{Guid.NewGuid():N}";
		Activity? span = null;
		using var activityListener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
			ActivityStopped = activity => span = activity,
		};
		ActivitySource.AddActivityListener(activityListener);

		var measurements = new ConcurrentQueue<(string Name, double Value, KeyValuePair<string, object?>[] Tags)>();
		using var meterListener = new MeterListener
		{
			InstrumentPublished = (instrument, listener) =>
			{
				if (instrument.Meter.Name == sourceName)
					listener.EnableMeasurementEvents(instrument);
			},
		};
		meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
			measurements.Enqueue((instrument.Name, value, tags.ToArray())));
		meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
			measurements.Enqueue((instrument.Name, value, tags.ToArray())));
		meterListener.Start();

		using var client = new T().AsBuilder()
			.UseOpenTelemetry(sourceName: sourceName, configure: telemetry => telemetry.EnableSensitiveData = false)
			.Build();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		const string prompt = "Reply with exactly one word: hello.";
		var updates = new List<ChatResponseUpdate>();
		ChatResponse response;
		if (streaming)
		{
			await foreach (var update in client.GetStreamingResponseAsync(prompt, cancellationToken: timeout.Token))
				updates.Add(update);
			response = updates.ToChatResponse();
		}
		else
		{
			response = await client.GetResponseAsync(prompt, cancellationToken: timeout.Token);
		}

		var metadata = client.GetService<ChatClientMetadata>();
		Assert.NotNull(metadata);
		Assert.NotNull(response.ModelId);
		Assert.NotNull(span);
		Assert.Equal(ActivityKind.Client, span.Kind);
		Assert.NotEqual(ActivityStatusCode.Error, span.Status);
		Assert.True(span.Duration > TimeSpan.Zero);
		Assert.Equal("chat", span.GetTagItem("gen_ai.operation.name"));
		Assert.Equal(metadata.ProviderName, span.GetTagItem("gen_ai.provider.name"));
		Assert.Equal(metadata.DefaultModelId, span.GetTagItem("gen_ai.request.model"));
		Assert.Equal(response.ModelId, span.GetTagItem("gen_ai.response.model"));
		Assert.Null(span.GetTagItem("gen_ai.input.messages"));
		Assert.Null(span.GetTagItem("gen_ai.output.messages"));

		var metrics = measurements.ToArray();
		Assert.All(metrics, metric =>
		{
			Assert.True(metric.Value >= 0);
			Assert.Equal("chat", metric.Tags.Single(tag => tag.Key == "gen_ai.operation.name").Value);
			Assert.Equal(metadata.ProviderName, metric.Tags.Single(tag => tag.Key == "gen_ai.provider.name").Value);
			Assert.Equal(metadata.DefaultModelId, metric.Tags.Single(tag => tag.Key == "gen_ai.request.model").Value);
		});
		var duration = Assert.Single(metrics, metric => metric.Name == "gen_ai.client.operation.duration");
		Assert.True(duration.Value > 0);
		Assert.Equal(response.ModelId, duration.Tags.Single(tag => tag.Key == "gen_ai.response.model").Value);
		Assert.Equal(streaming ? 1 : 0,
			metrics.Count(metric => metric.Name == "gen_ai.client.operation.time_to_first_chunk"));
		Assert.Equal(streaming ? updates.Count - 1 : 0,
			metrics.Count(metric => metric.Name == "gen_ai.client.operation.time_per_output_chunk"));

		var tokenMetrics = metrics.Where(metric => metric.Name == "gen_ai.client.token.usage").ToArray();
		if (response.Usage is { } usage)
		{
			AssertExpectedUsage(usage);
			Assert.Equal(usage.InputTokenCount, (long)Assert.IsType<int>(span.GetTagItem("gen_ai.usage.input_tokens")));
			Assert.Equal(usage.OutputTokenCount, (long)Assert.IsType<int>(span.GetTagItem("gen_ai.usage.output_tokens")));
			if (usage.CachedInputTokenCount is { } cachedInput)
				Assert.Equal(cachedInput, (long)Assert.IsType<int>(span.GetTagItem("gen_ai.usage.cache_read.input_tokens")));
			Assert.Equal(2, tokenMetrics.Length);
			foreach (var (tokenType, count) in new[] { ("input", usage.InputTokenCount), ("output", usage.OutputTokenCount) })
			{
				var metric = Assert.Single(tokenMetrics,
					metric => metric.Tags.Single(tag => tag.Key == "gen_ai.token.type").Value as string == tokenType);
				Assert.Equal((double)count!.Value, metric.Value);
				Assert.Equal(response.ModelId, metric.Tags.Single(tag => tag.Key == "gen_ai.response.model").Value);
			}
		}
		else
		{
			Assert.Empty(tokenMetrics);
			Assert.Null(span.GetTagItem("gen_ai.usage.input_tokens"));
			Assert.Null(span.GetTagItem("gen_ai.usage.output_tokens"));
			Assert.Null(span.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
		}

		output.WriteLine($"Provider: {metadata.ProviderName}; model: {response.ModelId}; streaming: {streaming}");
		output.WriteLine($"Usage: input={response.Usage?.InputTokenCount}, output={response.Usage?.OutputTokenCount}, " +
			$"total={response.Usage?.TotalTokenCount}, cached={response.Usage?.CachedInputTokenCount}, reasoning={response.Usage?.ReasoningTokenCount}");
		output.WriteLine($"Span tags: {string.Join(", ", span.TagObjects.Select(tag => tag.Key))}");
		foreach (var group in metrics.GroupBy(metric => metric.Name))
			output.WriteLine($"Metric: {group.Key}; measurements: {group.Count()}; values: {string.Join(", ", group.Select(metric => metric.Value))}");
		return response;
	}

	/// <summary>
	/// Validates usage according to the provider's support in the current environment.
	/// </summary>
	protected void AssertExpectedUsage(UsageDetails? usage)
	{
		if (!IsUsageAvailable)
		{
			Assert.Null(usage);
			return;
		}

		Assert.NotNull(usage);
		Assert.NotNull(usage.InputTokenCount);
		Assert.NotNull(usage.OutputTokenCount);
		Assert.NotNull(usage.TotalTokenCount);
		Assert.True(usage.InputTokenCount.Value >= 1);
		Assert.True(usage.OutputTokenCount.Value >= 1);
		Assert.True(usage.TotalTokenCount.Value >= usage.InputTokenCount.Value);
		Assert.True(usage.TotalTokenCount.Value >= usage.OutputTokenCount.Value);

		if (usage.CachedInputTokenCount is { } cachedInputTokenCount)
			Assert.True(cachedInputTokenCount >= 0);

		if (usage.ReasoningTokenCount is { } reasoningTokenCount)
			Assert.True(reasoningTokenCount >= 0);

		AssertProviderUsage(usage);
	}

	/// <summary>
	/// Validates provider-specific usage details when usage is available.
	/// </summary>
	protected virtual void AssertProviderUsage(UsageDetails usage)
	{
	}
}
