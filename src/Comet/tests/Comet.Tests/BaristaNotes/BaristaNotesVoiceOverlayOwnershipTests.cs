#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using CometSamples.BaristaNotes.Components;
using Xunit;

namespace Comet.Tests.BaristaNotes;

public sealed class BaristaNotesVoiceOverlayOwnershipTests
{
	static BaristaNotesVoiceOverlayOwnershipTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	sealed class CountingNode : Backend.FakeBackendNode
	{
		public CountingNode(string kind) : base(kind) { }

		public int DisposeCount { get; private set; }

		public override void Dispose()
		{
			DisposeCount++;
			base.Dispose();
		}
	}

	static readonly BackendContext Context = new(new EmptyServiceProvider());

	[Fact]
	public void CloseReopenRouteSwitchAndTerminalDispose_OwnExactlyOneActiveVoiceGeneration()
	{
		CometDevRegistry.Reset();
		CometDevRegistry.Enabled = true;
		try
		{
			var visible = new Signal<bool>(false);
			var collapsed = new Signal<bool>(false);
			ICometBackendNode Factory(View view)
				=> new CountingNode(view.GetType().Name);

			var overlay = new VoiceOverlayView(visible, collapsed);
			CometBackendBridge.Materialize(overlay, Factory, Context);
			AssertNoVoiceGeneration();

			visible.Value = true;
			ReactiveScheduler.FlushSync();
			AssertExpandedVoiceGeneration();

			collapsed.Value = true;
			ReactiveScheduler.FlushSync();
			AssertCollapsedVoiceGeneration();

			collapsed.Value = false;
			ReactiveScheduler.FlushSync();
			AssertExpandedVoiceGeneration();

			var firstGeneration = VoiceViews()
				.ToDictionary(
					view => view.AutomationId,
					view => Assert.IsType<CountingNode>(view.Node));
			Tap("voice_close");
			ReactiveScheduler.FlushSync();

			Assert.False(visible.Value);
			AssertNoVoiceGeneration();
			Assert.Equal(0, firstGeneration["voice_overlay_expanded"].DisposeCount);
			Assert.All(
				firstGeneration
					.Where(item => item.Key != "voice_overlay_expanded")
					.Select(item => item.Value),
				node => Assert.Equal(1, node.DisposeCount));

			visible.Value = true;
			ReactiveScheduler.FlushSync();
			AssertExpandedVoiceGeneration();
			var reopenedGeneration = VoiceViews()
				.ToDictionary(
					view => view.AutomationId,
					view => Assert.IsType<CountingNode>(view.Node));
			Assert.Same(
				firstGeneration["voice_overlay_expanded"],
				reopenedGeneration["voice_overlay_expanded"]);
			foreach (var automationId in new[]
			{
				"voice_close",
				"voice_collapse",
				"voice_expand",
				"voice_push_to_talk",
			})
			{
				Assert.NotSame(
					firstGeneration[automationId],
					reopenedGeneration[automationId]);
			}

			visible.Value = false;
			ReactiveScheduler.FlushSync();
			AssertNoVoiceGeneration();

			overlay.Dispose();
			Assert.Empty(CometDevRegistry.Snapshot());
		}
		finally
		{
			CometDevRegistry.Enabled = false;
			CometDevRegistry.Reset();
		}
	}

	static void Tap(string automationId)
	{
		var entry = Assert.Single(
			CometDevRegistry.Snapshot(),
			node => node.AutomationId == automationId);
		var view = Assert.IsType<Text>(CometDevRegistry.Find(entry.Id));
		var data = new GestureData(GestureState.Ended, default);
		Assert.NotNull(view.Node);
		Assert.NotNull(((Backend.FakeBackendNode)view.Node!).Sink);
		((Backend.FakeBackendNode)view.Node!).Sink!.OnGesture(GestureKind.Tap, in data);
	}

	static List<View> VoiceViews() =>
		CometDevRegistry.Snapshot()
			.Where(node => node.AutomationId?.StartsWith("voice_", StringComparison.Ordinal) == true)
			.Select(node => CometDevRegistry.Find(node.Id))
			.OfType<View>()
			.ToList();

	static void AssertExpandedVoiceGeneration()
	{
		AssertSingle("voice_overlay_expanded");
		Assert.DoesNotContain(
			CometDevRegistry.Snapshot(),
			node => node.AutomationId == "voice_overlay_collapsed");
		AssertSingle("voice_close");
		AssertSingle("voice_collapse");
		AssertSingle("voice_expand");
		AssertSingle("voice_push_to_talk");
	}

	static void AssertCollapsedVoiceGeneration()
	{
		AssertSingle("voice_overlay_collapsed");
		Assert.DoesNotContain(
			CometDevRegistry.Snapshot(),
			node => node.AutomationId == "voice_overlay_expanded");
		AssertSingle("voice_close");
		AssertSingle("voice_collapse");
		AssertSingle("voice_expand");
		AssertSingle("voice_push_to_talk");
	}

	static void AssertNoVoiceGeneration() =>
		Assert.DoesNotContain(
			CometDevRegistry.Snapshot(),
			node => node.AutomationId?.StartsWith("voice_", StringComparison.Ordinal) == true);

	static void AssertSingle(string automationId) =>
		Assert.Single(
			CometDevRegistry.Snapshot(),
			node => node.AutomationId == automationId);
}
