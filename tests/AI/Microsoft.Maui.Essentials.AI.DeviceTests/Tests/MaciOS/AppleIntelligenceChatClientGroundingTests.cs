#if IOS || MACCATALYST
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Foundation;
using Microsoft.Extensions.AI;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class AppleIntelligenceChatClientGroundingTests(ITestOutputHelper output)
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public record Reservation(string Code, string Departure, int Passengers);
	public record GroundedAnswer(bool Found, string Answer);

	[Theory]
	[InlineData(false, "VEL-482", "18:45", 3)]
	[InlineData(true, "VEL-482", "18:45", 3)]
	[InlineData(false, "FEN-913", "07:20", 2)]
	[InlineData(true, "FEN-913", "07:20", 2)]
	[InlineData(false, "OAK-205", "12:30", 1)]
	[InlineData(true, "OAK-205", "12:30", 1)]
	public async Task GetResponseAsync_SuppliedReservation_ExtractsExactFacts(
		bool streaming, string code, string departure, int passengers)
	{
		using IChatClient client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		var source = $"Reservation {code} departs at {departure} with {passengers} passengers.";
		var messages = new ChatMessage[]
		{
			new(ChatRole.System, "Extract the reservation facts from the supplied text. Copy the code and departure exactly."),
			new(ChatRole.User, source),
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<Reservation>(JsonOptions),
			MaxOutputTokens = 256,
		};

		var text = await RespondAsync(client, messages, options, streaming, timeout.Token);
		WriteObservation(nameof(GetResponseAsync_SuppliedReservation_ExtractsExactFacts), streaming, source, text);

		var actual = JsonSerializer.Deserialize<Reservation>(text, JsonOptions);
		Assert.Equal(new Reservation(code, departure, passengers), actual);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task GetResponseAsync_FactAbsentFromSource_ReturnsExplicitUnknown(bool streaming)
	{
		using IChatClient client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		const string source = "The Pine desk opens at 09:00 and closes at 17:00. What is the desk's access code?";
		var messages = new ChatMessage[]
		{
			new(ChatRole.System,
				"Answer using only the supplied text. If the answer is absent, set found to false and answer to an empty string. Do not invent a value."),
			new(ChatRole.User, source),
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<GroundedAnswer>(JsonOptions),
			MaxOutputTokens = 256,
		};

		var text = await RespondAsync(client, messages, options, streaming, timeout.Token);
		WriteObservation(nameof(GetResponseAsync_FactAbsentFromSource_ReturnsExplicitUnknown), streaming, source, text);

		var actual = JsonSerializer.Deserialize<GroundedAnswer>(text, JsonOptions);
		Assert.Equal(new GroundedAnswer(false, ""), actual);
	}

	[Fact]
	public async Task GetResponseAsync_SuppliedHistory_PreservesEarlierFact()
	{
		using IChatClient client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		var messages = new ChatMessage[]
		{
			new(ChatRole.System, "Answer from the conversation. Copy the requested label exactly."),
			new(ChatRole.User, "The label on the box is PINE-739."),
			new(ChatRole.Assistant, "Noted."),
			new(ChatRole.User, "What is the label on the box? Set found to true if it was supplied."),
		};
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema<GroundedAnswer>(JsonOptions),
			MaxOutputTokens = 256,
		};

		var response = await client.GetResponseAsync(messages, options, timeout.Token);
		WriteObservation(nameof(GetResponseAsync_SuppliedHistory_PreservesEarlierFact), false,
			string.Join("\n", messages.Select(message => $"{message.Role}: {message.Text}")), response.Text);

		var actual = JsonSerializer.Deserialize<GroundedAnswer>(response.Text, JsonOptions);
		Assert.Equal(new GroundedAnswer(true, "PINE-739"), actual);
	}

	[Fact]
	public async Task GetResponseAsync_OversizedPrompt_ReportsContextLimit()
	{
		using IChatClient client = new AppleIntelligenceChatClient();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		const string sentence = "The office keeps routine records in a cabinet near the door.";
		var prompt = "Read this English document:\n" +
			string.Join("\n", Enumerable.Repeat(sentence, 1024)) +
			"\nSummarize the document in one sentence.";
		var options = new ChatOptions { MaxOutputTokens = 64 };

		var error = await Assert.ThrowsAsync<NSErrorException>(() =>
			client.GetResponseAsync([new(ChatRole.User, prompt)], options, timeout.Token));
		WriteObservation(nameof(GetResponseAsync_OversizedPrompt_ReportsContextLimit), false,
			$"Read this English document:\\n[1024 newline-separated repetitions of '{sentence}']\\nSummarize the document in one sentence.",
			$"{error.Error.Domain}/{error.Error.Code}: {error.Message}");

		Assert.Contains("context", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<string> RespondAsync(
		IChatClient client, ChatMessage[] messages, ChatOptions options, bool streaming, CancellationToken cancellationToken)
	{
		if (!streaming)
			return (await client.GetResponseAsync(messages, options, cancellationToken)).Text;

		var text = new StringBuilder();
		await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
			text.Append(update.Text);
		return text.ToString();
	}

	private void WriteObservation(string test, bool streaming, string input, string response) =>
		output.WriteLine("APPLE_CHAT_EVALUATION " + JsonSerializer.Serialize(new
		{
			test,
			streaming,
			input,
			response,
			os = RuntimeInformation.OSDescription,
			architecture = RuntimeInformation.ProcessArchitecture.ToString(),
			sampling = "greedy (TopK unset)",
		}, JsonOptions));
}
#endif
