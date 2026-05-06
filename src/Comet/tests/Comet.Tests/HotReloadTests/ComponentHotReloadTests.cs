using System;
using Comet.HotReload;
using Microsoft.Maui.HotReload;
using Xunit;

namespace Comet.Tests
{
	public class ComponentHotReloadTests : TestBase
	{
		class CounterState
		{
			public int Count { get; set; }
		}

		class CounterProps
		{
			public string Label { get; set; } = "Count";
		}

		class StatefulCounterComponent : Component<CounterState>
		{
			public override View Render() => new Text($"Count: {State.Count}");

			public void SetCount(int value) => SetState(state => state.Count = value);
		}

		class StatefulCounterReplacementComponent : Component<CounterState>
		{
			public override View Render() => new Text($"Updated Count: {State.Count}");
		}

		class PropsCounterComponent : Component<CounterState, CounterProps>
		{
			public override View Render() => new Text($"{Props.Label}: {State.Count}");

			public void SetCount(int value) => SetState(state => state.Count = value);
		}

		class PropsCounterReplacementComponent : Component<CounterState, CounterProps>
		{
			public override View Render() => new Text($"Updated {Props.Label}: {State.Count}");
		}

		class CounterHostView : View
		{
			public readonly StatefulCounterComponent Counter = new StatefulCounterComponent();

			[Body]
			View body() => new VStack
			{
				new Text("Parent"),
				Counter,
			};
		}

		static void ResetHotReload()
		{
			ResetComet();
			MauiHotReloadHelper.IsEnabled = true;
		}

		[Fact]
		public void TransferStateFromMovesStateAcrossReplacementComponentTypes()
		{
			ResetHotReload();
			var original = new StatefulCounterComponent();
			original.SetViewHandlerToGeneric();
			original.SetCount(7);

			var replacement = new StatefulCounterReplacementComponent();
			((IComponentWithState)replacement).TransferStateFrom((IComponentWithState)original);

			Assert.Equal(7, replacement.State.Count);
			var rendered = Assert.IsType<Text>(replacement.GetView());
			Assert.Equal("Updated Count: 7", rendered.Value);
		}

		[Fact]
		public void TransferStateFromMovesPropsAndStateAcrossReplacementComponentTypes()
		{
			ResetHotReload();
			var original = new PropsCounterComponent
			{
				Props = new CounterProps
				{
					Label = "Score"
				}
			};
			original.SetViewHandlerToGeneric();
			original.SetCount(9);

			var replacement = new PropsCounterReplacementComponent();
			((IComponentWithState)replacement).TransferStateFrom((IComponentWithState)original);

			Assert.Equal("Score", replacement.Props.Label);
			Assert.Equal(9, replacement.State.Count);
			var rendered = Assert.IsType<Text>(replacement.GetView());
			Assert.Equal("Updated Score: 9", rendered.Value);
		}

		[Fact]
		public void HotReloadReplacesStatefulComponentAndPreservesState()
		{
			ResetHotReload();
			var original = new StatefulCounterComponent();
			original.SetViewHandlerToGeneric();
			InitializeHandlers(original);
			original.SetCount(7);

			CometHotReloadHelper.RegisterReplacedView(typeof(StatefulCounterComponent).FullName, typeof(StatefulCounterReplacementComponent));
			MauiHotReloadHelper.TriggerReload();

			var replacement = Assert.IsType<StatefulCounterReplacementComponent>(original.GetReplacedView());
			Assert.Equal(7, replacement.State.Count);

			var rendered = Assert.IsType<Text>(original.GetView());
			Assert.Equal("Updated Count: 7", rendered.Value);
		}

		[Fact]
		public void HotReloadReplacesPropsComponentAndPreservesPropsAndState()
		{
			ResetHotReload();
			var original = new PropsCounterComponent
			{
				Props = new CounterProps
				{
					Label = "Score"
				}
			};
			original.SetViewHandlerToGeneric();
			InitializeHandlers(original);
			original.SetCount(9);

			CometHotReloadHelper.RegisterReplacedView(typeof(PropsCounterComponent).FullName, typeof(PropsCounterReplacementComponent));
			MauiHotReloadHelper.TriggerReload();

			var replacement = Assert.IsType<PropsCounterReplacementComponent>(original.GetReplacedView());
			Assert.Equal("Score", replacement.Props.Label);
			Assert.Equal(9, replacement.State.Count);

			var rendered = Assert.IsType<Text>(original.GetView());
			Assert.Equal("Updated Score: 9", rendered.Value);
		}

		[Fact]
		public void HotReloadReplacesNestedComponentAndPreservesChildState()
		{
			ResetHotReload();
			var host = new CounterHostView();
			host.SetViewHandlerToGeneric();
			InitializeHandlers(host);
			host.Counter.SetCount(3);

			CometHotReloadHelper.RegisterReplacedView(typeof(StatefulCounterComponent).FullName, typeof(StatefulCounterReplacementComponent));
			MauiHotReloadHelper.TriggerReload();

			_ = host.Counter.GetView();
			var replacement = Assert.IsType<StatefulCounterReplacementComponent>(host.Counter.GetReplacedView() ?? host.Counter.GetView());
			Assert.Equal(3, replacement.State.Count);
		}
	}
}
