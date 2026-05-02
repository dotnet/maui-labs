using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Xunit;

namespace Comet.Tests
{
	public class ListViewTests : TestBase
	{
		// ---- ListView<T> core functionality ----

		[Fact]
		public void ListViewGeneric_RowsReturnsCorrectCount()
		{
			var items = new List<string> { "A", "B", "C" }.AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(3, ilv.Rows(0));
		}

		[Fact]
		public void ListViewGeneric_ViewForCallbackInvoked()
		{
			var items = new List<string> { "Hello", "World" }.AsReadOnly();
			var invokedItems = new List<string>();
			var lv = new ListView<string>(items);
			lv.ViewFor = item =>
			{
				invokedItems.Add(item);
				return new Text(item);
			};

			var ilv = (IListView)lv;
			var view0 = ilv.ViewFor(0, 0);
			var view1 = ilv.ViewFor(0, 1);

			Assert.Contains("Hello", invokedItems);
			Assert.Contains("World", invokedItems);
			Assert.NotNull(view0);
			Assert.NotNull(view1);
		}

		[Fact]
		public void ListViewGeneric_ItemAtReturnsCorrectItem()
		{
			var items = new List<string> { "First", "Second", "Third" }.AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal("First", ilv.ItemAt(0, 0));
			Assert.Equal("Second", ilv.ItemAt(0, 1));
			Assert.Equal("Third", ilv.ItemAt(0, 2));
		}

		[Fact]
		public void ListViewGeneric_HeaderAndFooterAccessible()
		{
			var items = new List<string> { "A" }.AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);
			lv.Header = new Text("Header");
			lv.Footer = new Text("Footer");

			var ilv = (IListView)lv;
			Assert.NotNull(ilv.HeaderView());
			Assert.NotNull(ilv.FooterView());
		}

		[Fact]
		public void ListViewGeneric_ItemSelectedCallbackFires()
		{
			var items = new List<string> { "A", "B", "C" }.AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			(object item, int section, int row) selectedResult = default;
			lv.ItemSelected = result => selectedResult = result;

			var ilv = (IListView)lv;
			ilv.OnSelected(0, 1);

			Assert.Equal("B", selectedResult.item);
			Assert.Equal(0, selectedResult.section);
			Assert.Equal(1, selectedResult.row);
		}

		[Fact]
		public void ListViewGeneric_SingleSection()
		{
			var items = new List<string> { "A", "B" }.AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(1, ilv.Sections());
		}

		// ---- ObservableCollection support ----

		[Fact]
		public void ListViewGeneric_ObservableCollectionAddTriggersChange()
		{
			var observable = new ObservableCollection<string> { "A", "B" };
			var lv = new ListView<string>(() => observable);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(2, ilv.Rows(0));

			observable.Add("C");
			Assert.Equal(3, ilv.Rows(0));
		}

		[Fact]
		public void ListViewGeneric_ObservableCollectionRemoveWorks()
		{
			var observable = new ObservableCollection<string> { "A", "B", "C" };
			var lv = new ListView<string>(() => observable);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(3, ilv.Rows(0));

			observable.Remove("B");
			Assert.Equal(2, ilv.Rows(0));
		}

		[Fact]
		public void ListViewGeneric_ObservableCollectionClearWorks()
		{
			var observable = new ObservableCollection<string> { "A", "B", "C" };
			var lv = new ListView<string>(() => observable);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(3, ilv.Rows(0));

			observable.Clear();
			Assert.Equal(0, ilv.Rows(0));
		}

		// ---- SectionedListView<T> ----

		[Fact]
		public void SectionedListView_MultipleSectionsCorrectCount()
		{
			var slv = new SectionedListView<string>();
			var section1 = new Section<string>(new List<string> { "A", "B" }.AsReadOnly())
			{
				ViewFor = item => new Text(item)
			};
			var section2 = new Section<string>(new List<string> { "C", "D", "E" }.AsReadOnly())
			{
				ViewFor = item => new Text(item)
			};
			slv.Add(section1);
			slv.Add(section2);

			var ilv = (IListView)slv;
			Assert.Equal(2, ilv.Sections());
			Assert.Equal(2, ilv.Rows(0));
			Assert.Equal(3, ilv.Rows(1));
		}

		[Fact]
		public void SectionedListView_SectionHeadersAndFooters()
		{
			var slv = new SectionedListView<string>();
			var section = new Section<string>(new List<string> { "A" }.AsReadOnly())
			{
				ViewFor = item => new Text(item),
				Header = new Text("Section Header"),
				Footer = new Text("Section Footer")
			};
			slv.Add(section);

			var ilv = (IListView)slv;
			Assert.NotNull(ilv.HeaderFor(0));
			Assert.NotNull(ilv.FooterFor(0));
		}

		[Fact]
		public void SectionedListView_ItemsFromCorrectSection()
		{
			var slv = new SectionedListView<string>();
			var section1 = new Section<string>(new List<string> { "S1A", "S1B" }.AsReadOnly())
			{
				ViewFor = item => new Text(item)
			};
			var section2 = new Section<string>(new List<string> { "S2A", "S2B", "S2C" }.AsReadOnly())
			{
				ViewFor = item => new Text(item)
			};
			slv.Add(section1);
			slv.Add(section2);

			var ilv = (IListView)slv;
			Assert.Equal("S1A", ilv.ItemAt(0, 0));
			Assert.Equal("S1B", ilv.ItemAt(0, 1));
			Assert.Equal("S2A", ilv.ItemAt(1, 0));
			Assert.Equal("S2C", ilv.ItemAt(1, 2));
		}

		// ---- ListView (non-generic) ----

		[Fact]
		public void ListView_AddViewItemsManually()
		{
			var lv = new ListView();
			lv.Add(new Text("Item 1"));
			lv.Add(new Text("Item 2"));
			lv.Add(new Text("Item 3"));

			var ilv = (IListView)lv;
			Assert.Equal(3, ilv.Rows(0));
		}

		[Fact]
		public void ListView_GetViewForReturnsCorrectView()
		{
			var lv = new ListView();
			var text1 = new Text("Item 1");
			var text2 = new Text("Item 2");
			lv.Add(text1);
			lv.Add(text2);

			var ilv = (IListView)lv;
			Assert.Same(text1, ilv.ViewFor(0, 0));
			Assert.Same(text2, ilv.ViewFor(0, 1));
		}

		[Fact]
		public void ListView_HeaderFooterProperties()
		{
			var lv = new ListView();
			lv.Header = new Text("Header");
			lv.Footer = new Text("Footer");

			Assert.NotNull(lv.Header);
			Assert.NotNull(lv.Footer);

			var ilv = (IListView)lv;
			Assert.Same(lv.Header, ilv.HeaderView());
			Assert.Same(lv.Footer, ilv.FooterView());
		}

		// ---- CollectionView<T> properties ----

		[Fact]
		public void CollectionView_DefaultVerticalLayout()
		{
			var cv = new CollectionView<string>();
			Assert.Equal(ItemsLayoutOrientation.Vertical, cv.ItemsLayout.Orientation);
		}

		[Fact]
		public void CollectionView_HorizontalLayoutWithSpacing()
		{
			var cv = new CollectionView<string>();
			cv.ItemsLayout = ItemsLayout.Horizontal(15);
			Assert.Equal(ItemsLayoutOrientation.Horizontal, cv.ItemsLayout.Orientation);
			Assert.Equal(15, cv.ItemsLayout.ItemSpacing);
		}

		[Fact]
		public void CollectionView_GridItemsLayoutSpan()
		{
			var cv = new CollectionView<string>();
			cv.ItemsLayout = GridItemsLayout.Vertical(3, 8);
			var grid = cv.ItemsLayout as GridItemsLayout;
			Assert.NotNull(grid);
			Assert.Equal(3, grid.Span);
			Assert.Equal(8, grid.ItemSpacing);
		}

		[Fact]
		public void CollectionView_SelectionModeSettings()
		{
			var cv = new CollectionView<string>();
			Assert.Equal(SelectionMode.Single, cv.SelectionMode);

			cv.SelectionMode = SelectionMode.None;
			Assert.Equal(SelectionMode.None, cv.SelectionMode);

			cv.SelectionMode = SelectionMode.Multiple;
			Assert.Equal(SelectionMode.Multiple, cv.SelectionMode);
		}

		[Fact]
		public void CollectionView_EmptyViewAssignment()
		{
			var cv = new CollectionView<string>();
			var emptyView = new Text("No items found");
			cv.EmptyView = emptyView;
			Assert.Same(emptyView, cv.EmptyView);
		}

		[Fact]
		public void CollectionView_ItemSizingStrategy()
		{
			var cv = new CollectionView<string>();
			Assert.Equal(ItemSizingStrategy.MeasureAllItems, cv.ItemSizingStrategy);

			cv.ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem;
			Assert.Equal(ItemSizingStrategy.MeasureFirstItem, cv.ItemSizingStrategy);
		}

		[Fact]
		public void CollectionView_WithItemsReturnsCorrectRows()
		{
			var items = new List<string> { "X", "Y", "Z" }.AsReadOnly();
			var cv = new CollectionView<string>(items);
			cv.ViewFor = item => new Text(item);

			var ilv = (IListView)cv;
			Assert.Equal(3, ilv.Rows(0));
		}

		[Fact]
		public void CollectionView_IsGroupedDefault()
		{
			var cv = new CollectionView<string>();
			Assert.False(cv.IsGrouped);
			cv.IsGrouped = true;
			Assert.True(cv.IsGrouped);
		}

		// ---- CarouselView<T> properties ----

		[Fact]
		public void CarouselView_DefaultHorizontalLayout()
		{
			var cv = new CarouselView<string>();
			Assert.Equal(ItemsLayoutOrientation.Horizontal, cv.ItemsLayout.Orientation);
		}

		[Fact]
		public void CarouselView_ConstructorWithItemsHorizontal()
		{
			var items = new List<string> { "Slide1", "Slide2" }.AsReadOnly();
			var cv = new CarouselView<string>(items);
			Assert.Equal(ItemsLayoutOrientation.Horizontal, cv.ItemsLayout.Orientation);
		}

		[Fact]
		public void CarouselView_PositionTracking()
		{
			var cv = new CarouselView<string>();
			cv.Position = 2;
			Assert.Equal(2, cv.Position?.CurrentValue);
		}

		[Fact]
		public void CarouselView_LoopProperty()
		{
			var cv = new CarouselView<string>();
			Assert.False(cv.Loop);
			cv.Loop = true;
			Assert.True(cv.Loop);
		}

		[Fact]
		public void CarouselView_PeekAreaInsets()
		{
			var cv = new CarouselView<string>();
			Assert.Equal(0, cv.PeekAreaInsets);
			cv.PeekAreaInsets = 30;
			Assert.Equal(30, cv.PeekAreaInsets);
		}

		[Fact]
		public void CarouselView_SwipeAndBounceDefaults()
		{
			var cv = new CarouselView<string>();
			Assert.True(cv.IsSwipeEnabled);
			Assert.True(cv.IsBounceEnabled);
			Assert.True(cv.IsScrollAnimated);
		}

		[Fact]
		public void CarouselView_DisableSwipeAndBounce()
		{
			var cv = new CarouselView<string>();
			cv.IsSwipeEnabled = false;
			cv.IsBounceEnabled = false;
			cv.IsScrollAnimated = false;
			Assert.False(cv.IsSwipeEnabled);
			Assert.False(cv.IsBounceEnabled);
			Assert.False(cv.IsScrollAnimated);
		}

		[Fact]
		public void CarouselView_PositionChangedCallback()
		{
			var cv = new CarouselView<string>();
			int changedPosition = -1;
			cv.PositionChanged = pos => changedPosition = pos;
			cv.PositionChanged?.Invoke(3);
			Assert.Equal(3, changedPosition);
		}

		[Fact]
		public void CarouselView_CurrentItemChangedCallback()
		{
			var cv = new CarouselView<string>();
			string changedItem = null;
			cv.CurrentItemChanged = item => changedItem = item;
			cv.CurrentItemChanged?.Invoke("Slide2");
			Assert.Equal("Slide2", changedItem);
		}

		// ---- Dispose behavior ----

		[Fact]
		public void ListView_DisposeDisposesViews()
		{
			var lv = new ListView();
			var text1 = new Text("A");
			var text2 = new Text("B");
			lv.Add(text1);
			lv.Add(text2);

			lv.Dispose();
			Assert.True(text1.IsDisposed);
			Assert.True(text2.IsDisposed);
		}

		[Fact]
		public void ListViewGeneric_DisposeDisposesCreatedViews()
		{
			var items = new List<string> { "A", "B" }.AsReadOnly();
			var lv = new ListView<string>(items);
			var createdViews = new List<View>();
			lv.ViewFor = item =>
			{
				var view = new Text(item);
				createdViews.Add(view);
				return view;
			};

			var ilv = (IListView)lv;
			ilv.ViewFor(0, 0);
			ilv.ViewFor(0, 1);

			Assert.Equal(2, createdViews.Count);
			lv.Dispose();
			Assert.All(createdViews, v => Assert.True(v.IsDisposed));
		}

		[Fact]
		public void SectionedListView_DisposeClearsSectionCache()
		{
			var slv = new SectionedListView<string>();
			var section = new Section<string>(new List<string> { "A" }.AsReadOnly())
			{
				ViewFor = item => new Text(item)
			};
			slv.Add(section);

			var ilv = (IListView)slv;
			ilv.ViewFor(0, 0);

			slv.Dispose();
			// After dispose, the sectioned list view should be disposed
			Assert.True(slv.IsDisposed);
		}

		[Fact]
		public void ListViewGeneric_ObservableCollectionUnsubscribedOnDispose()
		{
			var observable = new ObservableCollection<string> { "A" };
			var lv = new ListView<string>(() => observable);
			lv.ViewFor = item => new Text(item);

			lv.Dispose();

			// After dispose, adding to observable should not throw
			observable.Add("B");
			Assert.Equal(2, observable.Count);
		}

		// ---- Edge cases ----

		[Fact]
		public void ListViewGeneric_EmptyList()
		{
			var items = new List<string>().AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(0, ilv.Rows(0));
		}

		[Fact]
		public void ListView_EmptyNoViews()
		{
			var lv = new ListView();
			var ilv = (IListView)lv;
			Assert.Equal(0, ilv.Rows(0));
		}

		[Fact]
		public void ListViewGeneric_LargeList()
		{
			var items = Enumerable.Range(0, 1000).Select(i => $"Item {i}").ToList().AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(1000, ilv.Rows(0));
			Assert.Equal("Item 0", ilv.ItemAt(0, 0));
			Assert.Equal("Item 999", ilv.ItemAt(0, 999));
		}

		[Fact]
		public void ListViewGeneric_ViewForReturningNullHandled()
		{
			var items = new List<string> { "A", "B" }.AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => null;

			var ilv = (IListView)lv;
			var view = ilv.ViewFor(0, 0);
			Assert.Null(view);
		}

		[Fact]
		public void ListViewGeneric_ThrowsOnDirectAdd()
		{
			var lv = new ListView<string>();
			Assert.Throws<NotSupportedException>(() => lv.Add(new Text("X")));
		}

		[Fact]
		public void SectionedListView_ThrowsOnDirectAdd()
		{
			var slv = new SectionedListView<string>();
			Assert.Throws<NotSupportedException>(() => slv.Add(new Text("X")));
		}

		[Fact]
		public void SectionedListView_EmptyNoSections()
		{
			var slv = new SectionedListView<string>();
			var ilv = (IListView)slv;
			Assert.Equal(0, ilv.Sections());
		}

		[Fact]
		public void ListView_ShouldDisposeViewsProperty()
		{
			var lv = new ListView();
			var ilv = (IListView)lv;
			Assert.False(ilv.ShouldDisposeViews);
		}

		[Fact]
		public void ListViewGeneric_ShouldDisposeViewsTrue()
		{
			var items = new List<string> { "A" }.AsReadOnly();
			var lv = new ListView<string>(items);
			var ilv = (IListView)lv;
			Assert.True(ilv.ShouldDisposeViews);
		}

		[Fact]
		public void CollectionView_NonGenericDefaults()
		{
			var cv = new CollectionView();
			Assert.Equal(ItemsLayoutOrientation.Vertical, cv.ItemsLayout.Orientation);
			Assert.Equal(SelectionMode.Single, cv.SelectionMode);
			Assert.Equal(ItemSizingStrategy.MeasureAllItems, cv.ItemSizingStrategy);
			Assert.Null(cv.EmptyView);
		}

		[Fact]
		public void CarouselView_NonGenericDefaults()
		{
			var cv = new CarouselView();
			Assert.True(cv.IsBounceEnabled);
			Assert.True(cv.IsSwipeEnabled);
			Assert.True(cv.IsScrollAnimated);
			Assert.False(cv.Loop);
			Assert.Equal(0, cv.PeekAreaInsets);
		}

		[Fact]
		public void ListViewGeneric_CountFuncUsedWhenNoItems()
		{
			var lv = new ListView<string>();
			lv.Count = () => 5;
			lv.ItemFor = index => $"Item {index}";
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(5, ilv.Rows(0));
		}

		[Fact]
		public void SectionedListView_SectionForCallback()
		{
			var slv = new SectionedListView<string>();
			slv.SectionCount = () => 2;
			slv.SectionFor = index =>
			{
				var items = index == 0
					? new List<string> { "A", "B" }.AsReadOnly()
					: new List<string> { "C" }.AsReadOnly();
				return new Section<string>(items) { ViewFor = item => new Text(item) };
			};

			var ilv = (IListView)slv;
			Assert.Equal(2, ilv.Sections());
			Assert.Equal(2, ilv.Rows(0));
			Assert.Equal(1, ilv.Rows(1));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(1)]
		[InlineData(5)]
		[InlineData(100)]
		public void ListViewGeneric_VariousItemCounts(int count)
		{
			var items = Enumerable.Range(0, count).Select(i => $"Item {i}").ToList().AsReadOnly();
			var lv = new ListView<string>(items);
			lv.ViewFor = item => new Text(item);

			var ilv = (IListView)lv;
			Assert.Equal(count, ilv.Rows(0));
		}

		[Theory]
		[InlineData(SelectionMode.None)]
		[InlineData(SelectionMode.Single)]
		[InlineData(SelectionMode.Multiple)]
		public void CollectionView_AllSelectionModes(SelectionMode mode)
		{
			var cv = new CollectionView<string>();
			cv.SelectionMode = mode;
			Assert.Equal(mode, cv.SelectionMode);
		}
	}
}
