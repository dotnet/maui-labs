#nullable enable
using System;
using System.IO;
using Comet;
using Comet.Backend;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.Backend
{
	public class R1ButtonPaddingTests
	{
		static R1ButtonPaddingTests() =>
			ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

		static readonly BackendContext Context = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, view => new FakeBackendNode(view.GetType().Name), Context);

		static FakeBackendNode Node(View view) => (FakeBackendNode)view.Node!;

		[Fact]
		public void Button_UnsetAndExplicitZeroPaddingRemainDistinct()
		{
			var unset = Bridge(new Button("UNSET", () => { }));
			var zero = Bridge(new Button("ZERO", () => { }).Padding(Thickness.Zero));

			Assert.Equal(
				PropertyValueKind.None,
				unset.Get(PropertyIds.Button_HasExplicitPadding).Kind);
			Assert.True(zero.Get(PropertyIds.Button_HasExplicitPadding).AsBool);
			Assert.Equal(PropertyValueKind.None, zero.Get(PropertyIds.Padding).Kind);
		}

		[Fact]
		public void Button_CustomPaddingEmitsNativeOverrideAndExactEdges()
		{
			var padding = new Thickness(8, 18, 8, 34);
			var node = Bridge(new Button("SAVE", () => { }).Padding(padding));

			Assert.True(node.Get(PropertyIds.Button_HasExplicitPadding).AsBool);
			Assert.Equal(padding, node.Get(PropertyIds.Padding).AsObject);
		}

		[Fact]
		public void Button_RemovingExplicitPaddingRestoresNativeDefaultMode()
		{
			var padded = new Button("SAVE", () => { })
				.Padding(new Thickness(8, 18, 8, 34));
			var node = Bridge(padded);
			var replacement = new Button("SAVE", () => { });

			replacement.UpdateFromOldView(padded);

			Assert.Same(node, replacement.Node);
			Assert.False(node.Get(PropertyIds.Button_HasExplicitPadding).AsBool);
			Assert.Equal(default(Thickness), node.Get(PropertyIds.Padding).AsObject);
		}

		[Fact]
		public void ComposeExplicitButtonPadding_ContentMeasurePreservesNativeMinimumWithoutDefaultInsets()
		{
			var label = new Size(52, 20);
			var custom = new Thickness(8, 18, 8, 34);

			var unset = ButtonMeasurementContract.MeasureContent(
				label, Thickness.Zero, hasExplicitPadding: false);
			var explicitCustom = ButtonMeasurementContract.MeasureContent(
				label, custom, hasExplicitPadding: true);
			var explicitZero = ButtonMeasurementContract.MeasureContent(
				label, Thickness.Zero, hasExplicitPadding: true);

			Assert.Equal(new Size(100, 48), unset);
			Assert.Equal(new Size(52, 20), explicitCustom);
			Assert.Equal(new Size(52, 48), explicitZero);
		}

		[Fact]
		public void AutoRow_SourceActionPaddingAndMinimumProduceSeventyTwoPointOuterBox()
		{
			var button = new Button("BACK", () => { })
				.FontFamily("ManropeSemibold")
				.FontSize(18)
				.CharacterSpacing(1)
				.Padding(new Thickness(8, 18, 8, 34))
				.MinimumHeight(72);
			var root = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", "*" })
			{
				button.Cell(row: 0),
				new Spacer().Cell(row: 1),
			};
			Bridge(root);
			Node(button).MeasureResult = new Size(52, 20);

			CometBackendLayoutEngine.Layout(root, new Size(402, 300));

			Assert.Equal(72, Node(button).ArrangedFrame!.Value.Height, 3);
		}

		[Fact]
		public void SwiftUIYogaRouting_ButtonBypassesGenericOuterLeafPadding()
		{
			var root = FindCometRoot();
			Assert.NotNull(root);
			var source = File.ReadAllText(IOPath.Combine(
				root!,
				"src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift"));

			var containerSwitch = source.IndexOf("case \"vstack\":", StringComparison.Ordinal);
			var buttonCase = source.IndexOf("case \"button\":", containerSwitch, StringComparison.Ordinal);
			var textFieldCase = source.IndexOf("case \"textfield\":", buttonCase, StringComparison.Ordinal);
			Assert.True(containerSwitch >= 0 && buttonCase > containerSwitch && textFieldCase > buttonCase);

			var buttonRoute = source.Substring(buttonCase, textFieldCase - buttonCase);
			Assert.Contains("CometLeafContent(node: node)", buttonRoute);
			Assert.DoesNotContain(".padding(EdgeInsets", buttonRoute);
		}

		static string? FindCometRoot()
		{
			var directory = AppContext.BaseDirectory;
			for (var index = 0; index < 10 && directory is not null; index++)
			{
				if (File.Exists(IOPath.Combine(directory, "global.json")) &&
					Directory.Exists(IOPath.Combine(directory, "sample")))
					return directory;
				directory = IOPath.GetDirectoryName(directory);
			}
			return null;
		}

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
