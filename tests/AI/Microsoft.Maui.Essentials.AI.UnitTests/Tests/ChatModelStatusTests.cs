using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class ChatModelStatusTests
{
	[Fact]
	public void Availability_DefinesExpectedStates()
	{
		Assert.Equal(
			[
				ChatModelAvailability.Available,
				ChatModelAvailability.NotReady,
				ChatModelAvailability.NotSupported,
				ChatModelAvailability.Disabled,
				ChatModelAvailability.RequirementsNotMet,
				ChatModelAvailability.Unknown,
				ChatModelAvailability.Unavailable,
				ChatModelAvailability.Downloading,
			],
			Enum.GetValues<ChatModelAvailability>());
	}

	[Theory]
	[InlineData(ChatModelAvailability.Available, "Available")]
	[InlineData(ChatModelAvailability.NotReady, "NotReady")]
	[InlineData(ChatModelAvailability.NotSupported, "NotSupported")]
	[InlineData(ChatModelAvailability.Disabled, "Disabled")]
	[InlineData(ChatModelAvailability.RequirementsNotMet, "RequirementsNotMet")]
	[InlineData(ChatModelAvailability.Unknown, "Unknown")]
	[InlineData(ChatModelAvailability.Unavailable, "Unavailable")]
	[InlineData(ChatModelAvailability.Downloading, "Downloading")]
	public void Availability_ToStringReturnsStableName(ChatModelAvailability availability, string name) =>
		Assert.Equal(name, availability.ToString());

	[Fact]
	public void Capability_DefinesExpectedValues()
	{
		Assert.Equal(
			[
				ChatModelCapability.Text,
				ChatModelCapability.Streaming,
				ChatModelCapability.ImageInput,
				ChatModelCapability.ToolCalling,
				ChatModelCapability.StructuredOutput,
				ChatModelCapability.TokenUsage,
				ChatModelCapability.Reasoning,
			],
			Enum.GetValues<ChatModelCapability>());
	}

	[Theory]
	[InlineData(ChatModelCapability.Text, "Text")]
	[InlineData(ChatModelCapability.Streaming, "Streaming")]
	[InlineData(ChatModelCapability.ImageInput, "ImageInput")]
	[InlineData(ChatModelCapability.ToolCalling, "ToolCalling")]
	[InlineData(ChatModelCapability.StructuredOutput, "StructuredOutput")]
	[InlineData(ChatModelCapability.TokenUsage, "TokenUsage")]
	[InlineData(ChatModelCapability.Reasoning, "Reasoning")]
	public void Capability_ToStringReturnsStableName(ChatModelCapability capability, string name) =>
		Assert.Equal(name, capability.ToString());

	[Theory]
	[InlineData(ChatModelAvailability.Unavailable)]
	[InlineData(ChatModelAvailability.NotReady)]
	[InlineData(ChatModelAvailability.Downloading)]
	[InlineData(ChatModelAvailability.Unknown)]
	public void Status_PreservesAvailability(ChatModelAvailability availability)
	{
		var status = new ChatModelStatus(availability, null);

		Assert.Equal(availability, status.Availability);
	}

	[Fact]
	public void Status_DistinguishesUnknownFromKnownEmptyCapabilities()
	{
		var unknown = new ChatModelStatus(ChatModelAvailability.Available, null);
		var knownNone = new ChatModelStatus(ChatModelAvailability.Available, []);

		Assert.Null(unknown.Capabilities);
		Assert.NotNull(knownNone.Capabilities);
		Assert.Empty(knownNone.Capabilities);
	}

	[Fact]
	public void Status_DefensivelyCopiesCallerList()
	{
		List<ChatModelCapability> capabilities = [ChatModelCapability.Text, ChatModelCapability.Streaming];
		var status = new ChatModelStatus(ChatModelAvailability.Available, capabilities);

		capabilities[0] = ChatModelCapability.ImageInput;
		capabilities.Clear();

		Assert.Equal([ChatModelCapability.Text, ChatModelCapability.Streaming], status.Capabilities);
		Assert.Equal(ChatModelAvailability.Available, status.Availability);
	}

	[Fact]
	public void Status_DefensivelyCopiesCallerArrayAndReadOnlyWrapper()
	{
		var capabilities = new[] { ChatModelCapability.Text };
		var fromArray = new ChatModelStatus(ChatModelAvailability.Available, capabilities);
		var fromWrapper = new ChatModelStatus(
			ChatModelAvailability.Available, Array.AsReadOnly(capabilities));

		capabilities[0] = ChatModelCapability.ImageInput;

		Assert.Equal(ChatModelCapability.Text, Assert.Single(fromArray.Capabilities!));
		Assert.Equal(ChatModelCapability.Text, Assert.Single(fromWrapper.Capabilities!));
	}

	[Fact]
	public void Status_CapabilitiesCannotBeMutatedThroughCollectionInterfaces()
	{
		var status = new ChatModelStatus(
			ChatModelAvailability.Available, [ChatModelCapability.Text]);
		var list = Assert.IsAssignableFrom<IList<ChatModelCapability>>(status.Capabilities);

		Assert.True(list.IsReadOnly);
		Assert.Throws<NotSupportedException>(() => list[0] = ChatModelCapability.ImageInput);
		Assert.Throws<NotSupportedException>(() => list.Add(ChatModelCapability.ImageInput));
		Assert.Throws<NotSupportedException>(() => list.Clear());
		Assert.Equal(ChatModelCapability.Text, Assert.Single(status.Capabilities!));
	}

	[Fact]
	public void Status_DoesNotExposeGeneratedRecordMembers()
	{
		Assert.False(typeof(IEquatable<ChatModelStatus>).IsAssignableFrom(typeof(ChatModelStatus)));
		var methods = typeof(ChatModelStatus).GetMethods(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Static |
			System.Reflection.BindingFlags.DeclaredOnly);

		Assert.Equal(
			["get_Availability", "get_Capabilities"],
			methods.Select(method => method.Name).OrderBy(name => name));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(0)]
	[InlineData(1)]
	public void Status_EquivalentSnapshotsUseReferenceIdentity(int capabilityCount)
	{
		ChatModelCapability[]? capabilities = capabilityCount switch
		{
			-1 => null,
			0 => [],
			_ => [ChatModelCapability.Text],
		};
		var first = new ChatModelStatus(ChatModelAvailability.Available, capabilities);
		var second = new ChatModelStatus(ChatModelAvailability.Available, capabilities);

		Assert.Equal(first.Availability, second.Availability);
		Assert.Equal(first.Capabilities, second.Capabilities);
		Assert.False(first.Equals(second));
		Assert.False(first == second);
		Assert.True(first.Equals(first));
	}

	[Fact]
	public void Status_HasNoPublicConstructorsOrPropertySetters()
	{
		Assert.True(typeof(ChatModelStatus).IsSealed);
		Assert.Empty(typeof(ChatModelStatus).GetConstructors());
		Assert.All(typeof(ChatModelStatus).GetProperties(), property => Assert.Null(property.SetMethod));
	}
}
