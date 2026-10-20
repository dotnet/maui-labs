#if IOS || MACCATALYST
using Foundation;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Tests for AppleIntelligenceChatClient edge cases in message conversion,
/// tool validation, and error handling paths.
/// </summary>
[Collection(nameof(AppleIntelligenceCollection))]
public class AppleIntelligenceChatClientValidationTests
{
	// Apple's managed profile rejects dictionaries before native setup or invocation, unlike cloud schemas.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ToolSchema_WithDictionaryArguments_RejectsBeforeInvocation(bool streaming)
	{
		var calls = 0;
		string ReadDictionary(Dictionary<string, string> values)
		{
			Interlocked.Increment(ref calls);
			return "unused";
		}
		var tool = AIFunctionFactory.Create(ReadDictionary, "dictionary_arguments");
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions { Tools = [tool] };

		var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync([new(ChatRole.User, "Hello")], options).ToChatResponseAsync();
			else
				await client.GetResponseAsync([new(ChatRole.User, "Hello")], options);
		});

		Assert.Contains(tool.Name, error.Message, StringComparison.Ordinal);
		Assert.Contains("$.properties.values.additionalProperties", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, calls);
	}

	// Apple's adapter compiles only arguments; a dictionary return schema never drives native Tool generation.
	[Fact]
	public void ToNative_ToolWithFormattedParametersAndDictionaryReturn_CompilesOnlyArguments()
	{
		Dictionary<string, string> CaptureFormatted(Guid id, DateTimeOffset timestamp) =>
			new() { ["result"] = "FORMATTED_CAPTURED" };

		var tool = AIFunctionFactory.Create(CaptureFormatted, "capture_formatted");
		var properties = tool.JsonSchema.GetProperty("properties");
		Assert.Equal("uuid", properties.GetProperty("id").GetProperty("format").GetString());
		Assert.Equal("date-time", properties.GetProperty("timestamp").GetProperty("format").GetString());
		Assert.True(tool.ReturnJsonSchema.HasValue);
		Assert.True(tool.ReturnJsonSchema.Value.TryGetProperty("additionalProperties", out _));

		using var client = new AppleIntelligenceChatClient();
		var native = client.ToNative(new ChatOptions { Tools = [tool] }, default);

		Assert.NotNull(native);
		var adapter = Assert.Single(native.Tools!);
		Assert.Equal(AppleJsonSchemaCompiler.Shared.Compile(tool.JsonSchema, AppleJsonSchemaMode.Tool), adapter.ArgumentsSchema);
		using var schema = JsonDocument.Parse(adapter.ArgumentsSchema);
		Assert.Equal("AppleSchema0", schema.RootElement.GetProperty("title").GetString());
		Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
		Assert.Equal("string", schema.RootElement.GetProperty("properties").GetProperty("id").GetProperty("type").GetString());
		Assert.DoesNotContain("\"format\"", adapter.ArgumentsSchema, StringComparison.Ordinal);
	}

	// Apple's transcript retains empty user turns as text segments; inference policy is tested separately.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ToNative_WithOnlyUnconvertibleUserContent_RetainsEmptyTurn(bool nullText)
	{
		AIContent content = nullText ? new TextContent(null) : new UnrecognizedContent();

		var message = Assert.Single(AppleIntelligenceChatClient.ToNative(
			[new ChatMessage(ChatRole.User, [content])], null));

		Assert.Equal(ChatRoleNative.User, message.Role);
		Assert.Equal(string.Empty, Assert.IsType<TextContentNative>(Assert.Single(message.Contents)).Text);
	}

	private sealed class UnrecognizedContent : AIContent;

	// Apple's response profile requires even optional CLR fields; tool schemas deliberately retain omission.
	[Fact]
	public void ResponseSchema_WithOptionalProperties_RequiresAllNativeFields()
	{
		var format = Assert.IsType<ChatResponseFormatJson>(
			ChatResponseFormat.ForJsonSchema<OptionalResponse>(JsonSerializerOptions.Web));
		Assert.NotNull(format.Schema);
		Assert.False(format.Schema.Value.TryGetProperty("required", out _));

		using var document = JsonDocument.Parse(AppleJsonSchemaCompiler.Shared.Compile(format.Schema!.Value, AppleJsonSchemaMode.Response));
		var schema = document.RootElement;

		var properties = schema.GetProperty("properties");
		Assert.Equal(4, properties.EnumerateObject().Count());
		var required = schema.GetProperty("required").EnumerateArray()
			.Select(value => value.GetString()).ToArray();
		Assert.Equal(4, required.Length);
		foreach (var name in new[] { "summary", "keyPoints", "category", "sentiment" })
		{
			Assert.True(properties.TryGetProperty(name, out _));
			Assert.Contains(name, required);
		}
		Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
	}

	// Apple's closed-shape policy must reject the original dictionary response before any native callback.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ResponseSchema_WithDictionary_RejectsInManagedCode(bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<Dictionary<string, string>>() };
		var error = await Assert.ThrowsAsync<AppleJsonSchemaException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync([new(ChatRole.User, "Hello")], options).ToChatResponseAsync();
			else
				await client.GetResponseAsync([new(ChatRole.User, "Hello")], options);
		});
		Assert.Equal("$.additionalProperties", error.Path);
	}

	// Inspects Apple's native payload, including the MEAI-generated nested nullable and optional parameter contract.
	[Fact]
	public void ToNative_MeaiNestedNullableFormattedParameters_UsesCanonicalProfile()
	{
		var tool = AIFunctionFactory.Create(CaptureSchemaValues, "capture_schema_values");
		using var client = new AppleIntelligenceChatClient();
		var native = client.ToNative(new ChatOptions
		{
			Tools = [tool],
			ResponseFormat = ChatResponseFormat.ForJsonSchema<SchemaResponse>(JsonSerializerOptions.Web)
		}, default)!;
		var adapter = Assert.Single(native.Tools!);
		using var arguments = JsonDocument.Parse(adapter.ArgumentsSchema);
		Assert.Equal("[\"count\",\"id\",\"nested\",\"timestamp\"]", arguments.RootElement.GetProperty("required").GetRawText());
		var properties = arguments.RootElement.GetProperty("properties");
		Assert.Equal("null", properties.GetProperty("label").GetProperty("anyOf")[1].GetProperty("type").GetString());
		Assert.Equal("null", properties.GetProperty("count").GetProperty("anyOf")[1].GetProperty("type").GetString());
		Assert.DoesNotContain("\"format\"", adapter.ArgumentsSchema, StringComparison.Ordinal);
		Assert.DoesNotContain("\"default\"", adapter.ArgumentsSchema, StringComparison.Ordinal);
		using var response = JsonDocument.Parse((string)native.ResponseJsonSchema!);
		Assert.Equal("[\"id\",\"nested\",\"timestamp\"]", response.RootElement.GetProperty("required").GetRawText());
	}

	// Crosses Foundation Models' real managed-to-native profile boundary; nullable native schemas need OS 26.4.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponse_MeaiNestedNullableFormattedSchema_MapsAndGenerates(bool streaming)
	{
		if (!(OperatingSystem.IsIOSVersionAtLeast(26, 4) || OperatingSystem.IsMacCatalystVersionAtLeast(26, 4)))
			return;
		using var client = new AppleIntelligenceChatClient();
		var calls = 0;
		SchemaResponse Capture(Guid id, DateTimeOffset timestamp, SchemaNested nested, int? count, string? label = null)
		{
			Interlocked.Increment(ref calls);
			return CaptureSchemaValues(id, timestamp, nested, count, label);
		}
		var tool = AIFunctionFactory.Create(Capture, "capture_schema_values");
		var options = new ChatOptions
		{
			Tools = [tool],
			ToolMode = OperatingSystem.IsIOSVersionAtLeast(27) || OperatingSystem.IsMacCatalystVersionAtLeast(27)
				? ChatToolMode.RequireSpecific(tool.Name) : ChatToolMode.Auto,
			ResponseFormat = ChatResponseFormat.ForJsonSchema<SchemaResponse>(JsonSerializerOptions.Web),
			Temperature = 0
		};
		const string prompt = """
			Call capture_schema_values exactly once with id 12345678-1234-1234-1234-123456789abc,
			timestamp 2026-10-20T12:30:00+00:00, nested {"count":null,"label":"sample"},
			count null, and omit optional label. Then return its id, timestamp and nested values.
			""";
		var response = streaming
			? await client.GetStreamingResponseAsync([new(ChatRole.User, prompt)], options).ToChatResponseAsync()
			: await client.GetResponseAsync([new(ChatRole.User, prompt)], options);
		var value = JsonSerializer.Deserialize<SchemaResponse>(response.Text!, JsonSerializerOptions.Web);
		Assert.NotNull(value);
		Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), value.Id);
		Assert.Equal(DateTimeOffset.Parse("2026-10-20T12:30:00+00:00"), value.Timestamp);
		Assert.NotNull(value.Nested);
		Assert.Null(value.Nested.Count);
		Assert.Equal("sample", value.Nested.Label);
		Assert.Equal(1, calls);
	}

	private static SchemaResponse CaptureSchemaValues(Guid id, DateTimeOffset timestamp, SchemaNested nested, int? count, string? label = null) =>
		new() { Id = id, Timestamp = timestamp, Nested = nested };

	public sealed class SchemaResponse
	{
		public Guid Id { get; set; }
		public DateTimeOffset Timestamp { get; set; }
		public SchemaNested? Nested { get; set; }
	}

	public sealed class SchemaNested
	{
		public int? Count { get; set; }
		public string? Label { get; set; }
	}

	// The Apple bridge verifies authoritative final JSON before either completion handler can report success.
	[Fact]
	public void ValidateStructuredResponse_NativeGuideViolation_RejectsFinalValueButNotSchemaFreeText()
	{
		using var document = JsonDocument.Parse("""{"type":"number","minimum":1e-200}""");
		using var schema = new NSString(AppleJsonSchemaCompiler.Shared.Compile(document.RootElement, AppleJsonSchemaMode.Response));
		var invalid = new ChatResponse(new ChatMessage(ChatRole.Assistant, "0"));
		var error = Assert.Throws<InvalidDataException>(() => AppleIntelligenceChatClient.ValidateStructuredResponse(invalid, schema));
		Assert.Contains("$.minimum", error.Message, StringComparison.Ordinal);
		Assert.Same(invalid, AppleIntelligenceChatClient.ValidateStructuredResponse(invalid, null));
		var valid = new ChatResponse(new ChatMessage(ChatRole.Assistant, "1e-200"));
		Assert.Same(valid, AppleIntelligenceChatClient.ValidateStructuredResponse(valid, schema));
	}

	// Native Tool arguments must satisfy Apple's compiled profile before AIFunction conversion or user code.
	[Theory]
	[InlineData("{}")]
	[InlineData("{\"count\":null}")]
	[InlineData("{\"count\":1.2}")]
	[InlineData("{\"count\":1,\"extra\":0}")]
	public async Task ToolArguments_NativeGuideViolation_DoesNotInvokeFunction(string arguments)
	{
		var calls = 0;
		string Count(int count) { calls++; return "counted"; }
		var function = AIFunctionFactory.Create(Count, "count_values");
		using var client = new AppleIntelligenceChatClient();
		var adapter = Assert.Single(client.ToNative(new ChatOptions { Tools = [function] }, default)!.Tools!);
		var completion = new TaskCompletionSource<(bool HasResult, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var payload = new NSString(arguments);
		adapter.CallWithArguments(payload, (result, error) =>
		{
			completion.TrySetResult((result is not null, error?.LocalizedDescription));
		});
		var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.False(result.HasResult);
		Assert.NotNull(result.Error);
		Assert.Contains("Tool 'count_values' arguments", result.Error, StringComparison.Ordinal);
		Assert.Equal(0, calls);
	}

	// Apple's native strict-schema path forces all optional fields across repeated requests; JSON Schema generally permits omission.
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_AndStreaming_WithRequiredNativeSchema_ReturnAllFields()
	{
		var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<OptionalResponse>(JsonSerializerOptions.Web)
		};

		foreach (var prompt in new[]
		{
			"Describe a rainbow in one sentence.",
			"Name three colors in a rainbow.",
			"Explain briefly how rainbows form."
		})
		{
			var messages = new List<ChatMessage> { new(ChatRole.User, prompt) };

			var response = await client.GetResponseAsync(messages, options);
			using var fullJson = JsonDocument.Parse(response.Text!);
			AssertFourFieldResponse(fullJson.RootElement);

			var text = new System.Text.StringBuilder();
			await foreach (var update in client.GetStreamingResponseAsync(messages, options))
				text.Append(update.Text);

			using var streamedJson = JsonDocument.Parse(text.ToString());
			AssertFourFieldResponse(streamedJson.RootElement);
		}
	}

	private static void AssertFourFieldResponse(JsonElement root)
	{
		Assert.Equal(JsonValueKind.Object, root.ValueKind);
		Assert.Equal(4, root.EnumerateObject().Count());
		Assert.Equal(JsonValueKind.String, root.GetProperty("summary").ValueKind);
		Assert.Equal(JsonValueKind.Array, root.GetProperty("keyPoints").ValueKind);
		Assert.Equal(JsonValueKind.String, root.GetProperty("category").ValueKind);
		var sentiment = root.GetProperty("sentiment").GetString();
		Assert.Contains(sentiment, new[]
		{
			nameof(OptionalSentiment.Neutral),
			nameof(OptionalSentiment.Positive),
			nameof(OptionalSentiment.Negative),
			nameof(OptionalSentiment.Mixed)
		});
	}

	public sealed class OptionalResponse
	{
		public string Summary { get; set; } = string.Empty;

		public List<string> KeyPoints { get; set; } = [];

		public string Category { get; set; } = string.Empty;

		public OptionalSentiment Sentiment { get; set; }
	}

	[JsonConverter(typeof(JsonStringEnumConverter<OptionalSentiment>))]
	public enum OptionalSentiment
	{
		Neutral,
		Positive,
		Negative,
		Mixed,
	}

	// Inspects Apple's native tool-registration list and its ToolMode.None short-circuit before AIFunction validation.
	[Fact]
	public void ToNative_ToolModeNone_DoesNotRegisterTools()
	{
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.None,
			Tools = [new UnsupportedToolForTesting()]
		};

		var nativeOptions = new AppleIntelligenceChatClient().ToNative(options, CancellationToken.None);

		Assert.NotNull(nativeOptions);
		Assert.Null(nativeOptions.Tools);
	}

	// Inspects Apple's native callback registration list; cloud clients use request tool envelopes instead.
	[Fact]
	public void ToNative_ToolModeAuto_RegistersTools()
	{
		var options = new ChatOptions
		{
			ToolMode = ChatToolMode.Auto,
			Tools = [AIFunctionFactory.Create(() => "result", "sample_tool")]
		};

		var nativeOptions = new AppleIntelligenceChatClient().ToNative(options, CancellationToken.None);

		Assert.NotNull(nativeOptions);
		Assert.NotNull(nativeOptions.Tools);
		Assert.Single(nativeOptions.Tools!);
	}

	/// <summary>
	/// Verifies that passing a non-AIFunction tool (e.g., a custom AITool subclass)
	/// throws NotSupportedException with a descriptive message listing the unsupported types.
	/// Apple native callback registration requires AIFunction; its diagnostic type listing is not a cloud error contract.
	/// </summary>
	[Fact]
	public async Task GetResponseAsync_WithNonAIFunctionTool_ThrowsNotSupportedException()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "Hello")
		};
		var options = new ChatOptions
		{
			Tools = [new UnsupportedToolForTesting()]
		};

		var ex = await Assert.ThrowsAsync<NotSupportedException>(
			() => client.GetResponseAsync(messages, options));
		Assert.Contains("AIFunction", ex.Message, StringComparison.Ordinal);
		Assert.Contains("UnsupportedToolForTesting", ex.Message, StringComparison.Ordinal);
	}

	// Apple's native transcript rejects orphaned tool outputs; fail deterministically before invoking the model.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task GetResponseAsync_WithOrphanedFunctionResult_ThrowsArgumentException(bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			// Orphaned result — callId "call-999" was never in a FunctionCallContent
			new(ChatRole.Tool, [new FunctionResultContent("call-999", "Unknown result")]),
			new(ChatRole.User, "Tell me more")
		};

		var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages);
		});
		Assert.Equal("messages", error.ParamName);
		Assert.Contains("call-999", error.Message, StringComparison.Ordinal);
		Assert.Contains("preceding assistant function call", error.Message, StringComparison.Ordinal);
	}

	// Only preceding assistant calls belong to Apple's tool transcript, not ignored unknown roles or future calls.
	[Theory]
	[InlineData("custom", false, false)]
	[InlineData("custom", false, true)]
	[InlineData("assistant", true, false)]
	[InlineData("assistant", true, true)]
	public async Task GetResponseAsync_WithResultWithoutPrecedingAssistantCall_Throws(string callRole, bool callAfterResult, bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		var call = new ChatMessage(new ChatRole(callRole), [new FunctionCallContent("call-1", "GetWeather")]);
		var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]);
		ChatMessage[] messages = callAfterResult
			? [result, call, new(ChatRole.User, "Hello")]
			: [call, result, new(ChatRole.User, "Hello")];

		var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync(messages).ToChatResponseAsync();
			else
				await client.GetResponseAsync(messages);
		});
		Assert.Equal("messages", error.ParamName);
		Assert.Contains("call-1", error.Message, StringComparison.Ordinal);
	}

	// Apple's result marshaller must resolve valid calls in encounter order without accepting names from ignored roles.
	[Fact]
	public void ToNative_WithValidFunctionHistory_PreservesCallNamesAndResults()
	{
		ChatMessage[] messages =
		[
			new(ChatRole.Assistant, [new TextContent("before"), new UnrecognizedContent(),
				new FunctionCallContent("call-1", "GetWeather"), new TextContent("after")]),
			new(new ChatRole("custom"), [new FunctionCallContent("call-1", "IgnoredCustomCall")]),
			new(ChatRole.Tool, [new UnrecognizedContent(), new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.Assistant, [new FunctionCallContent("call-2", "GetTime")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-2", "Noon")]),
			new(ChatRole.User, "Summarize.")
		];

		var native = AppleIntelligenceChatClient.ToNative(messages, null);
		Assert.Collection(native[0].Contents,
			content => Assert.Equal("before", Assert.IsType<TextContentNative>(content).Text),
			content => Assert.Equal("GetWeather", Assert.IsType<FunctionCallContentNative>(content).Name),
			content => Assert.Equal("after", Assert.IsType<TextContentNative>(content).Text));
		var results = native.SelectMany(message => message.Contents).OfType<FunctionResultContentNative>().ToArray();

		Assert.Collection(results,
			result =>
			{
				Assert.Equal("call-1", result.CallId);
				Assert.Equal("GetWeather", result.Name);
				Assert.Equal("Sunny", result.Result);
			},
			result =>
			{
				Assert.Equal("call-2", result.CallId);
				Assert.Equal("GetTime", result.Name);
				Assert.Equal("Noon", result.Result);
			});
	}

	public static IEnumerable<object[]> InvalidSamplingOptions()
	{
		(string Name, float Value)[] cases =
		[
			(nameof(ChatOptions.TopK), 0), (nameof(ChatOptions.TopK), -1),
			(nameof(ChatOptions.TopP), -0.1f), (nameof(ChatOptions.TopP), 1.1f),
			(nameof(ChatOptions.TopP), float.NaN), (nameof(ChatOptions.TopP), float.PositiveInfinity),
			(nameof(ChatOptions.TopP), float.NegativeInfinity),
			(nameof(ChatOptions.Temperature), -0.1f), (nameof(ChatOptions.Temperature), 1.1f),
			(nameof(ChatOptions.Temperature), 2f), (nameof(ChatOptions.Temperature), float.NaN),
			(nameof(ChatOptions.Temperature), float.PositiveInfinity),
			(nameof(ChatOptions.Temperature), float.NegativeInfinity)
		];
		foreach (var (name, value) in cases)
		{
			yield return [name, value, false];
			yield return [name, value, true];
		}
	}

	// Invalid Apple sampling parameters can trap in Swift; both APIs must reject them before any native generation.
	[Theory]
	[MemberData(nameof(InvalidSamplingOptions))]
	public async Task GetResponseAsync_WithInvalidSamplingOption_ThrowsBeforeInference(string name, float value, bool streaming)
	{
		using var client = new AppleIntelligenceChatClient();
		var options = new ChatOptions();
		if (name == nameof(ChatOptions.TopK))
			options.TopK = (int)value;
		else if (name == nameof(ChatOptions.TopP))
			options.TopP = value;
		else
			options.Temperature = value;

		var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
		{
			if (streaming)
				await client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options).ToChatResponseAsync();
			else
				await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options);
		});
		Assert.Contains(name, error.Message, StringComparison.Ordinal);
	}

	// These are Apple's boxed native sampling fields; Swift's deterministic mapping tests verify the resulting GenerationOptions.
	[Theory]
	[InlineData(null, null, null, null)]
	[InlineData(null, null, null, 0.7f)]
	[InlineData(null, null, 42L, null)]
	[InlineData(null, 0.75f, 42L, 0f)]
	[InlineData(10, null, 42L, 1f)]
	[InlineData(10, 0.75f, 42L, 0.5f)]
	[InlineData(null, 0f, null, null)]
	[InlineData(null, 1f, null, null)]
	public void ToNative_WithValidSamplingOptions_PreservesValues(int? topK, float? topP, long? seed, float? temperature)
	{
		var native = new AppleIntelligenceChatClient().ToNative(new ChatOptions
		{
			TopK = topK,
			TopP = topP,
			Seed = seed,
			Temperature = temperature
		}, default);

		Assert.NotNull(native);
		Assert.Equal(topK, native.TopK?.Int32Value);
		Assert.Equal((double?)topP, native.TopP?.DoubleValue);
		Assert.Equal(seed, native.Seed?.Int64Value);
		Assert.Equal((double?)temperature, native.Temperature?.DoubleValue);
	}

	/// <summary>
	/// Verifies that FunctionCallContent with empty Name populates callIdToName
	/// with an empty string, and subsequent FunctionResultContent for that CallId
	/// gets the empty name. This is the closest we can test to null since
	/// FunctionCallContent validates name is not null in its constructor.
	/// Apple's native name lookup tolerates empty names; cloud function schemas need not accept them.
	/// </summary>
	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task GetResponseAsync_WithFunctionCallEmptyName_DoesNotThrow()
	{
		var client = new AppleIntelligenceChatClient();
		var messages = new List<ChatMessage>
		{
			new(ChatRole.User, "What's the weather?"),
			new(ChatRole.Assistant, [new FunctionCallContent("call-1", "")]),
			new(ChatRole.Tool, [new FunctionResultContent("call-1", "Sunny")]),
			new(ChatRole.User, "Tell me more")
		};

		// Should not throw — empty Name means callIdToName has empty value for "call-1"
		var response = await client.GetResponseAsync(messages);
		Assert.NotNull(response);
	}

	/// <summary>
	/// A custom AITool subclass that is NOT an AIFunction, used to test validation.
	/// </summary>
	private sealed class UnsupportedToolForTesting : AITool;

}

#endif
