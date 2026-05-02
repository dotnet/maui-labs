using System;
using System.Collections.Generic;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Xunit;

namespace Comet.Tests
{
	public class PickerTests : TestBase
	{
		[Fact]
		public void PickerCreatedWithItems()
		{
			var picker = new Picker(0, "Apple", "Banana", "Cherry");

			var items = picker.Items.CurrentValue;
			Assert.NotNull(items);
			Assert.Equal(3, items.Count);
			Assert.Equal("Apple", items[0]);
			Assert.Equal("Banana", items[1]);
			Assert.Equal("Cherry", items[2]);
		}

		[Fact]
		public void PickerSelectedIndex()
		{
			var picker = new Picker(1, "Apple", "Banana", "Cherry");

			Assert.Equal(1, picker.SelectedIndex.CurrentValue);
		}

		[Fact]
		public void PickerSelectedIndexBinding()
		{
			var state = new Reactive<int>(0);
			var picker = new Picker(state, "Apple", "Banana", "Cherry");

			Assert.Equal(0, picker.SelectedIndex.CurrentValue);
		}

		[Fact]
		public void PickerItemsBinding()
		{
			var picker = new Picker();
			var items = new List<string> { "One", "Two", "Three" };
			picker.Items = items;

			Assert.Equal(3, picker.Items.CurrentValue.Count);
			Assert.Equal("One", picker.Items.CurrentValue[0]);
		}

		[Fact]
		public void PickerTitleBinding()
		{
			var picker = new Picker();
			picker.Title = "Select a fruit";

			Assert.Equal("Select a fruit", picker.Title.CurrentValue);
		}

		[Fact]
		public void PickerDefaultSelectedIndex()
		{
			var picker = new Picker();

			IPicker ipicker = picker;
			Assert.Equal(-1, ipicker.SelectedIndex);
		}

		[Fact]
		public void IPickerItemsProperty()
		{
			var picker = new Picker(0, "Apple", "Banana");

			IPicker ipicker = picker;
			Assert.Equal(2, ipicker.Items.Count);
			Assert.Equal("Apple", ipicker.Items[0]);
			Assert.Equal("Banana", ipicker.Items[1]);
		}

		[Fact]
		public void IPickerSelectedIndexProperty()
		{
			var picker = new Picker(2, "A", "B", "C");

			IPicker ipicker = picker;
			Assert.Equal(2, ipicker.SelectedIndex);
		}

		[Fact]
		public void IPickerTitleProperty()
		{
			var picker = new Picker();
			picker.Title = "Pick one";

			IPicker ipicker = picker;
			Assert.Equal("Pick one", ipicker.Title);
		}

		[Fact]
		public void IPickerItemDelegateGetCount()
		{
			var picker = new Picker(0, "X", "Y", "Z");

			var itemDelegate = (IItemDelegate<string>)picker;
			Assert.Equal(3, itemDelegate.GetCount());
		}

		[Fact]
		public void IPickerItemDelegateGetItem()
		{
			var picker = new Picker(0, "X", "Y", "Z");

			var itemDelegate = (IItemDelegate<string>)picker;
			Assert.Equal("X", itemDelegate.GetItem(0));
			Assert.Equal("Y", itemDelegate.GetItem(1));
			Assert.Equal("Z", itemDelegate.GetItem(2));
		}

		[Fact]
		public void IPickerItemDelegateGetItemOutOfRange()
		{
			var picker = new Picker(0, "X");

			var itemDelegate = (IItemDelegate<string>)picker;
			Assert.Null(itemDelegate.GetItem(-1));
			Assert.Null(itemDelegate.GetItem(5));
		}

		[Fact]
		public void PickerEmptyItemsReturnsEmptyList()
		{
			var picker = new Picker();

			IPicker ipicker = picker;
			Assert.NotNull(ipicker.Items);
			Assert.Empty(ipicker.Items);
		}

		[Fact]
		public void PickerSetSelectedIndexOnDefaultConstructorDoesNotThrow()
		{
			var picker = new Picker();
			IPicker ipicker = picker;

			// Should not throw NullReferenceException
			var exception = Record.Exception(() => ipicker.SelectedIndex = 0);
			Assert.Null(exception);
		}
	}
}
