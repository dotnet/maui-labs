#nullable enable
using System;
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	public class R1FontImageBackendTests
	{
		const string Alias = "R1FontImageAlias";
		const string Face = "R1FontImageFace-SemiBold";

		static R1FontImageBackendTests()
		{
			ThreadHelper.SetFireOnMainThread(action => action?.Invoke());
			FontFamilyRegistry.Register(Alias, Face, FontWeight.Semibold);
		}

		static readonly BackendContext Context = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, view => new FakeBackendNode(view.GetType().Name), Context);

		[Fact]
		public void FontAlias_ResolvesRegisteredFace_AndDirectFacePassesThrough()
		{
			var registration = FontFamilyRegistry.Resolve(Alias);
			var direct = FontFamilyRegistry.Resolve("DirectPostScriptFace-Regular");

			Assert.Equal(Face, registration.FaceName);
			Assert.Equal(FontWeight.Semibold, registration.PreferredWeight);
			Assert.Equal("DirectPostScriptFace-Regular", direct.FaceName);
			Assert.Null(direct.PreferredWeight);
		}

		[Fact]
		public void TextAndButton_EmitResolvedFace_WithoutOverridingExplicitWeight()
		{
			var textNode = Bridge(new Text("alias").FontFamily(Alias));
			var buttonNode = Bridge(new Button("direct", () => { })
				.FontFamily(Alias)
				.FontWeight(FontWeight.Bold));

			Assert.Equal(Face, textNode.Get(PropertyIds.Text_FontFamily).AsString);
			Assert.Equal((int)FontWeight.Semibold, textNode.Get(PropertyIds.Text_FontWeight).AsInt);
			Assert.Equal(Face, buttonNode.Get(PropertyIds.Text_FontFamily).AsString);
			Assert.Equal((int)FontWeight.Bold, buttonNode.Get(PropertyIds.Text_FontWeight).AsInt);
		}

		[Fact]
		public void FontImageSource_ObjectInitializer_EmitsTypedRasterParameters()
		{
			var source = new FontImageSource
			{
				Glyph = "\uefef",
				FontFamily = Alias,
				Size = 32,
				Color = Colors.Red,
			};

			var node = Bridge(new Image(source));

			Assert.Equal(string.Empty, node.Get(PropertyIds.Image_Source).AsString);
			Assert.Empty((byte[])node.Get(PropertyIds.Image_Data).AsObject!);
			Assert.Equal("\uefef", node.Get(PropertyIds.Image_FontGlyph).AsString);
			Assert.Equal(Face, node.Get(PropertyIds.Image_FontFamily).AsString);
			Assert.Equal(32, node.Get(PropertyIds.Image_FontSize).AsDouble);
			Assert.Equal((int)FontWeight.Semibold, node.Get(PropertyIds.Image_FontWeight).AsInt);
			Assert.False(node.Get(PropertyIds.Image_FontItalic).AsBool);
			Assert.Equal(Colors.Red, node.Get(PropertyIds.Image_FontColor).AsColor);
		}

		[Fact]
		public void FontImageSource_ReactiveReplacementUpdatesGlyphColorSizeAndDirectFace()
		{
			var current = new Signal<IImageSource>(new FontImageSource
			{
				Glyph = "\uefef",
				FontFamily = Alias,
				Size = 32,
				Color = Colors.Red,
			});
			var image = new Image(() => current.Value);
			var node = Bridge(image);

			current.Value = new FontImageSource
			{
				Glyph = "\uf009",
				FontFamily = "DirectPostScriptFace-Regular",
				Size = 18,
				Color = Colors.Blue,
			};
			ReactiveScheduler.FlushSync();

			Assert.Equal("\uf009", node.Get(PropertyIds.Image_FontGlyph).AsString);
			Assert.Equal(
				"DirectPostScriptFace-Regular",
				node.Get(PropertyIds.Image_FontFamily).AsString);
			Assert.Equal(18, node.Get(PropertyIds.Image_FontSize).AsDouble);
			Assert.Equal(Colors.Blue, node.Get(PropertyIds.Image_FontColor).AsColor);
		}

		[Fact]
		public void FontImageSource_ReplacedByFile_ClearsFontRasterState()
		{
			var fontImage = new Image(new FontImageSource
			{
				Glyph = "\uefef",
				FontFamily = Alias,
				Size = 32,
				Color = Colors.Red,
			});
			var node = Bridge(fontImage);
			var fileImage = new Image(new FileImageSource { File = "coffee.png" });

			fileImage.UpdateFromOldView(fontImage);

			Assert.Same(node, fileImage.Node);
			Assert.Equal("coffee.png", node.Get(PropertyIds.Image_Source).AsString);
			Assert.Equal(string.Empty, node.Get(PropertyIds.Image_FontGlyph).AsString);
			Assert.Equal(string.Empty, node.Get(PropertyIds.Image_FontFamily).AsString);
			Assert.Equal(0, node.Get(PropertyIds.Image_FontSize).AsDouble);
			Assert.Null(node.Get(PropertyIds.Image_FontColor).AsColor);
		}

		[Fact]
		public void FontImageScaleMetrics_AutoScalingEnlargesLogicalMeasure()
		{
			var scale = new FontImageScaleMetrics(deviceDensity: 2, scaledDensity: 3);

			Assert.Equal(2f, scale.RasterDensity(autoScaling: false));
			Assert.Equal(3f, scale.RasterDensity(autoScaling: true));
			Assert.Equal(
				20d,
				scale.PixelsToLogical(20 * scale.RasterDensity(autoScaling: false)),
				3);
			Assert.Equal(
				30d,
				scale.PixelsToLogical(20 * scale.RasterDensity(autoScaling: true)),
				3);
		}

		[Theory]
		[InlineData(0, (int)NativeFontWeightClass.Regular)]
		[InlineData(100, (int)NativeFontWeightClass.Thin)]
		[InlineData(200, (int)NativeFontWeightClass.UltraLight)]
		[InlineData(300, (int)NativeFontWeightClass.Light)]
		[InlineData(400, (int)NativeFontWeightClass.Regular)]
		[InlineData(500, (int)NativeFontWeightClass.Medium)]
		[InlineData(600, (int)NativeFontWeightClass.Semibold)]
		[InlineData(700, (int)NativeFontWeightClass.Bold)]
		[InlineData(800, (int)NativeFontWeightClass.Heavy)]
		[InlineData(900, (int)NativeFontWeightClass.Black)]
		public void NativeFontWeightMapping_CoversFullSwiftWeightScale(
			int weight,
			int expected)
		{
			Assert.Equal((NativeFontWeightClass)expected, NativeFontWeightMapping.Classify(weight));
		}

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
