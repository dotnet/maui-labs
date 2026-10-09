#nullable enable
using System.Linq;
using Comet;
using Comet.Backend;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Contract tests for the M4 (Jetcaster) control surface: the FilterChip control's
	/// typed-patch emit, and the ListView grid/carousel flavor properties the Compose
	/// node branches on (LazyVerticalGrid / M3 carousels vs plain LazyColumn/LazyRow).
	/// </summary>
	public class BackendJetcasterControlTests
	{
		static BackendJetcasterControlTests()
			=> ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		[Fact]
		public void FilterChip_EmitsSelectedState()
		{
			bool clicked = false;
			var chip = new FilterChip(selected: true, onClick: () => clicked = true,
				label: new Text("Society & Culture"));

			var node = (FakeBackendNode)CometBackendBridge.Materialize(
				chip, v => new FakeBackendNode("FilterChip"), Ctx);

			Assert.True(node.Get(PropertyIds.Toggle_IsOn).AsBool);
			chip.OnClick();
			Assert.True(clicked);
		}

		[Fact]
		public void FilterChip_ExposesSlotChildren()
		{
			var label = new Text("Arts");
			var leading = new Icon("check");
			var chip = new FilterChip(false, () => { }, label, leading);

			var children = ((IContainerView)chip).GetChildren();
			Assert.Equal(new View[] { label, leading }, children);
			Assert.Same(chip, label.Parent);
			Assert.Same(chip, leading.Parent);
		}

		[Fact]
		public void IconButton_ExposesSlotAndClick()
		{
			bool clicked = false;
			var icon = new Icon("playlist_add");
			var button = new IconButton(() => clicked = true, icon);

			Assert.Equal(new View[] { icon }, ((IContainerView)button).GetChildren());
			Assert.Same(button, icon.Parent);
			button.OnClick();
			Assert.True(clicked);
		}

		[Fact]
		public void ListView_GridAndCarouselFlavorsFlowThroughTheInterface()
		{
			var list = new ListView<int>(() => Enumerable.Range(0, 4).ToList())
			{
				ViewFor = i => new Text($"{i}"),
				GridAdaptiveMinWidth = 362,
			};
			IListView lv = list;
			Assert.Equal(362, lv.GridAdaptiveMinWidth);
			Assert.Equal(ListCarousel.None, lv.Carousel);

			var carousel = new ListView<int>(() => Enumerable.Range(0, 4).ToList())
			{
				ViewFor = i => new Text($"{i}"),
				Horizontal = true,
				Carousel = ListCarousel.MultiBrowse,
				CarouselItemWidth = 220,
			};
			IListView cv = carousel;
			Assert.True(cv.Horizontal);
			Assert.Equal(ListCarousel.MultiBrowse, cv.Carousel);
			Assert.Equal(220, cv.CarouselItemWidth);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
