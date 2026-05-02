using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Xunit;

namespace Comet.Tests
{
	public class NavigationTests : TestBase
	{
		// ---- CometShell Tests ----

		[Fact]
		public void ShellRouteRegistration()
		{
			CometShell.RegisterRoute("test-page", typeof(TestShellPage));
			Assert.True(CometShell.HasRoute("test-page"));
		}

		[Fact]
		public void ShellItemCreation()
		{
			var item = new ShellItem
			{
				Title = "Home",
				Route = "home"
			};
			Assert.Equal("Home", item.Title);
			Assert.Equal("home", item.Route);
		}

		[Fact]
		public void ShellSectionCreation()
		{
			var section = new ShellSection
			{
				Title = "Section",
				Route = "section"
			};
			Assert.Equal("Section", section.Title);
		}

		[Fact]
		public void ShellContentCreation()
		{
			var content = new ShellContent
			{
				Title = "Content",
				Route = "content",
				ContentTemplate = () => new Text("Hello Shell")
			};
			Assert.Equal("Content", content.Title);
			var view = content.ContentTemplate();
			Assert.NotNull(view);
		}

		[Fact]
		public void ShellHierarchy()
		{
			var shell = new CometShell();
			var item = new ShellItem { Title = "Main", Route = "main" };
			var section = new ShellSection { Title = "Home", Route = "home" };
			var content = new ShellContent
			{
				Title = "Dashboard",
				Route = "dashboard",
				ContentTemplate = () => new Text("Dashboard")
			};

			section.Items.Add(content);
			item.Items.Add(section);
			shell.Items.Add(item);

			Assert.Single(shell.Items);
			Assert.Single(shell.Items[0].Items);
			Assert.Single(shell.Items[0].Items[0].Items);
		}

		[Fact]
		public void ShellQueryParameters()
		{
			var parsed = CometShell.ParseQueryString("page?id=5&name=test");
			Assert.Equal("5", parsed["id"]);
			Assert.Equal("test", parsed["name"]);
		}

		class TestShellPage : View
		{
			public TestShellPage() { }
		}
	}
}
