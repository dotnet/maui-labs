using Comet.DevTools;
using Xunit;

namespace Comet.Tests
{
	public class CometDevAgentTests
	{
		[Theory]
		[InlineData("\"300\"")]
		[InlineData("null")]
		[InlineData("true")]
		[InlineData("1.5")]
		[InlineData("0")]
		[InlineData("10001")]
		public void DragAction_InvalidDuration_DoesNotInvokeDrag(string duration)
		{
			var calls = 0;
			CometDevRegistry.DragInjector = (_, _, _, _, _) =>
			{
				calls++;
				return true;
			};

			try
			{
				var result = CometDevAgent.DragAction(
					$"{{\"x1\":1,\"y1\":2,\"x2\":3,\"y2\":4,\"durationMs\":{duration}}}");

				Assert.Contains("\"success\":false", result);
				Assert.Equal(0, calls);
			}
			finally
			{
				CometDevRegistry.DragInjector = null;
			}
		}

		[Theory]
		[InlineData("", 300)]
		[InlineData(",\"durationMs\":1", 1)]
		[InlineData(",\"durationMs\":10000", 10000)]
		public void DragAction_ValidDuration_InvokesDrag(string durationProperty, int expectedDuration)
		{
			var actualDuration = 0;
			CometDevRegistry.DragInjector = (_, _, _, _, duration) =>
			{
				actualDuration = duration;
				return true;
			};

			try
			{
				var result = CometDevAgent.DragAction(
					$"{{\"x1\":1,\"y1\":2,\"x2\":3,\"y2\":4{durationProperty}}}");

				Assert.Contains("\"success\":true", result);
				Assert.Equal(expectedDuration, actualDuration);
			}
			finally
			{
				CometDevRegistry.DragInjector = null;
			}
		}
	}
}
