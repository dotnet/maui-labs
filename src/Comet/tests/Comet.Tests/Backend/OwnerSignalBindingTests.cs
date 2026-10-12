#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comet.Backend;
using Comet.Reactive;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.Backend;

public class OwnerSignalBindingTests
{
	static OwnerSignalBindingTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	[Fact]
	public void ScrollOwnerStateBridge_TransferRoutesUpdatesOnlyToCurrentOwner()
	{
		var outgoing = new ScrollView();
		var replacement = new ScrollView();
		var outgoingOffsetChanges = 0;
		var replacementOffsetChanges = 0;
		outgoing.ScrollOffset.PropertyChanged += (_, _) => outgoingOffsetChanges++;
		replacement.ScrollOffset.PropertyChanged += (_, _) => replacementOffsetChanges++;

		using var bridge = new ScrollOwnerStateBridge(outgoing);
		bridge.Publish(12, atTop: false);

		Assert.Equal(12, outgoing.ScrollOffset.Peek());
		Assert.False(outgoing.AtTop.Peek());
		Assert.Equal(1, outgoingOffsetChanges);

		bridge.TransferOwner(replacement);
		bridge.Publish(24, atTop: false);

		Assert.Equal(12, outgoing.ScrollOffset.Peek());
		Assert.Equal(1, outgoingOffsetChanges);
		Assert.Equal(24, replacement.ScrollOffset.Peek());
		Assert.False(replacement.AtTop.Peek());
		Assert.Equal(1, replacementOffsetChanges);

		bridge.TransferOwner(replacement);
		bridge.Publish(36, atTop: true);

		Assert.Equal(12, outgoing.ScrollOffset.Peek());
		Assert.Equal(1, outgoingOffsetChanges);
		Assert.Equal(36, replacement.ScrollOffset.Peek());
		Assert.True(replacement.AtTop.Peek());
		Assert.Equal(2, replacementOffsetChanges);

		bridge.Dispose();
		bridge.Publish(48, atTop: false);
		Assert.Equal(36, replacement.ScrollOffset.Peek());
		Assert.Equal(2, replacementOffsetChanges);
	}

	[Fact]
	public void FabExtendedStateBinding_TransferUnhooksOldSignalAndUsesNewOwner()
	{
		var oldSignal = new Signal<bool>(true);
		var newSignal = new Signal<bool>(false);
		var oldFab = CreateFab(oldSignal);
		var replacementFab = CreateFab(newSignal);
		var observed = new List<bool>();

		using var binding = new FabExtendedStateBinding(oldFab, observed.Add);
		Assert.Equal(new[] { true }, observed);
		Assert.Equal(1, PropertyChangedSubscriberCount(oldSignal));

		oldSignal.Value = false;
		Assert.Equal(new[] { true, false }, observed);

		binding.TransferOwner(replacementFab);
		Assert.Equal(new[] { true, false, false }, observed);
		Assert.Equal(0, PropertyChangedSubscriberCount(oldSignal));
		Assert.Equal(1, PropertyChangedSubscriberCount(newSignal));

		oldSignal.Value = true;
		Assert.Equal(new[] { true, false, false }, observed);

		newSignal.Value = true;
		Assert.Equal(new[] { true, false, false, true }, observed);

		binding.TransferOwner(replacementFab);
		Assert.Equal(1, PropertyChangedSubscriberCount(newSignal));

		binding.Dispose();
		Assert.Equal(0, PropertyChangedSubscriberCount(newSignal));
	}

	[Fact]
	public void PlatformOwnerTransferNodes_UseSharedCurrentOwnerBindings()
	{
		var projectRoot = IOPath.GetFullPath(IOPath.Combine(
			AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
		var composeScroll = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "Compose", "ComposeScrollNode.cs"));
		var swiftScroll = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIScrollNode.cs"));
		var composeFab = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "Compose", "ComposeFabNode.cs"));
		var swiftFab = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIFabNode.cs"));

		Assert.Contains("_scrollSignals.TransferOwner(scroll)", composeScroll);
		Assert.Contains("_scrollSignals.Publish(dp, px == 0)", composeScroll);
		Assert.DoesNotContain("offsetSignal = sv.ScrollOffset", composeScroll);
		Assert.Contains("_scrollSignals.TransferOwner(scroll)", swiftScroll);
		Assert.Contains("_scrollSignals.Publish(offset, offset <= 1.0)", swiftScroll);

		Assert.Contains("_extendedBinding.TransferOwner(fab)", composeFab);
		Assert.Contains("_extendedBinding.TransferOwner(fab)", swiftFab);
		var swiftOwnerChange = swiftFab[
			swiftFab.IndexOf("public void OnOwnerViewChanged", StringComparison.Ordinal)..];
		Assert.True(
			swiftOwnerChange.IndexOf("_fab = fab;", StringComparison.Ordinal) <
			swiftOwnerChange.IndexOf(
				"if (!isHotReload && string.IsNullOrEmpty(newView.GetKey()))",
				StringComparison.Ordinal));
	}

	static Fab CreateFab(Signal<bool> signal)
		=> new Fab(
			new Text("icon"),
			new Text("label"),
			() => { },
			56)
			.ExtendedWhen(signal);

	static int PropertyChangedSubscriberCount<T>(Signal<T> signal)
	{
		var field = typeof(Signal<T>).GetField(
			nameof(Signal<T>.PropertyChanged),
			BindingFlags.Instance | BindingFlags.NonPublic);
		var handlers = field?.GetValue(signal) as Delegate;
		return handlers?.GetInvocationList().Length ?? 0;
	}
}
