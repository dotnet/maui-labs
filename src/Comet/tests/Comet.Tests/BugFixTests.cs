using System;
using System.Windows.Input;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	public class BugFixTests : TestBase
	{
		// ---- ContainerView parent chain fix ----

		[Fact]
		public void ContainerView_IndexSetter_SetsParentOnNewView()
		{
			var container = new VStack();
			var original = new Text("Original");
			container.Add(original);

			var replacement = new Text("Replacement");
			container[0] = replacement;

			Assert.Same(container, replacement.Parent);
		}

		[Fact]
		public void ContainerView_IndexSetter_ClearsParentOnOldView()
		{
			var container = new VStack();
			var original = new Text("Original");
			container.Add(original);

			var replacement = new Text("Replacement");
			container[0] = replacement;

			Assert.Null(original.Parent);
		}

		// ---- WebView navigation callbacks ----

		[Fact]
		public void WebView_OnNavigated_CallbackFires()
		{
			string navigatedUrl = null;
			var wv = new WebView
			{
				OnNavigated = url => navigatedUrl = url
			};

			((IWebView)wv).Navigated(WebNavigationEvent.NewPage, "https://example.com", WebNavigationResult.Success);

			Assert.Equal("https://example.com", navigatedUrl);
		}

		[Fact]
		public void WebView_OnNavigating_CallbackFires()
		{
			string navigatingUrl = null;
			var wv = new WebView
			{
				OnNavigating = url => navigatingUrl = url
			};

			((IWebView)wv).Navigating(WebNavigationEvent.NewPage, "https://example.com");

			Assert.Equal("https://example.com", navigatingUrl);
		}

		// ---- SwipeView complete API ----

		[Fact]
		public void SwipeView_AllDirectionsWorkTogether()
		{
			var swipe = new SwipeView();
			swipe.Add(new Text("Content"));
			swipe.LeftItems = new SwipeItems { new SwipeItem { Text = "L" } };
			swipe.RightItems = new SwipeItems { new SwipeItem { Text = "R" } };
			swipe.TopItems = new SwipeItems { new SwipeItem { Text = "T" } };
			swipe.BottomItems = new SwipeItems { new SwipeItem { Text = "B" } };

			Assert.Equal(1, swipe.LeftItems.Count);
			Assert.Equal(1, swipe.RightItems.Count);
			Assert.Equal(1, swipe.TopItems.Count);
			Assert.Equal(1, swipe.BottomItems.Count);
			Assert.Equal("L", swipe.LeftItems[0].Text);
			Assert.Equal("R", swipe.RightItems[0].Text);
			Assert.Equal("T", swipe.TopItems[0].Text);
			Assert.Equal("B", swipe.BottomItems[0].Text);
		}

		[Fact]
		public void SwipeItem_CommandPattern_Works()
		{
			bool executed = false;
			object receivedParam = null;
			var item = new SwipeItem
			{
				Text = "Delete",
				Command = new TestCommand(p =>
				{
					executed = true;
					receivedParam = p;
				}),
				CommandParameter = "item-42"
			};

			item.Command.Execute(item.CommandParameter);

			Assert.True(executed);
			Assert.Equal("item-42", receivedParam);
		}

		// ---- ModalView ClearDelegates ----

		[Fact]
		public void ModalView_ClearDelegates_ResetsBothDelegates()
		{
			ModalView.PerformDismiss = () => { };
			ModalView.PerformPresent = v => { };

			Assert.NotNull(ModalView.PerformDismiss);
			Assert.NotNull(ModalView.PerformPresent);

			ModalView.ClearDelegates();

			Assert.Null(ModalView.PerformDismiss);
			Assert.Null(ModalView.PerformPresent);
		}

		// ---- RefreshView content management ----

		[Fact]
		public void RefreshView_Add_SetsContentAndParent()
		{
			var rv = new RefreshView();
			var content = new Text("Hello");
			rv.Add(content);

			Assert.Same(content, rv.Content);
			Assert.Same(rv, content.Parent);
		}

		[Fact]
		public void RefreshView_Dispose_ClearsContent()
		{
			var rv = new RefreshView();
			rv.Add(new Text("Content"));
			Assert.NotNull(rv.Content);

			rv.Dispose();

			Assert.Null(rv.Content);
		}

		private class TestCommand : ICommand
		{
			private readonly Action<object> _execute;
			public TestCommand(Action<object> execute) => _execute = execute;
			public event EventHandler CanExecuteChanged;
			public bool CanExecute(object parameter) => true;
			public void Execute(object parameter) => _execute(parameter);
		}
	}
}
