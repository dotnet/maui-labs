using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[Collection("Shell navigation")]
public class ShellSectionNavigationObserverTests
{
	[Fact]
	public async Task PushAndPop_DisplayTopPageAndThenRoot()
	{
		var (section, root) = CreateSection();
		Page? displayed = null;
		using var observer = new ShellSectionNavigationObserver(section, page => displayed = page);
		observer.Refresh();
		Assert.Same(root, displayed);

		var detail = new ContentPage();
		await section.Push(detail);
		Assert.Same(detail, displayed);
		await section.Pop();
		Assert.Same(root, displayed);
	}

	[Fact]
	public async Task PopToRoot_DisplaysRootEvenWhenRequestPrecedesStackReset()
	{
		var (section, root) = CreateSection();
		Page? displayed = null;
		using var observer = new ShellSectionNavigationObserver(section, page => displayed = page);
		await section.Push(new ContentPage());
		await section.Push(new ContentPage());
		await section.PopToRoot();
		Assert.Same(root, displayed);
		Assert.Single(section.Stack);
	}

	[Fact]
	public async Task InsertAndRemove_KeepTheTopPageDisplayed()
	{
		var (section, root) = CreateSection();
		Page? displayed = null;
		using var observer = new ShellSectionNavigationObserver(section, page => displayed = page);
		var detail = new ContentPage();
		var inserted = new ContentPage();
		await section.Push(detail);
		section.Insert(inserted, detail);
		Assert.Same(detail, displayed);
		section.Remove(detail);
		Assert.Same(inserted, displayed);
		section.Remove(inserted);
		Assert.Same(root, displayed);
	}

	[Fact]
	public async Task Refresh_WithExistingStack_DoesNotCreateRootTemplate()
	{
		var (section, _) = CreateSection();
		var creations = 0;
		section.CurrentItem = new ShellContent { ContentTemplate = new DataTemplate(() =>
		{
			creations++;
			return new ContentPage();
		}) };
		var detail = new ContentPage();
		await section.Push(detail);
		var before = creations;
		Page? displayed = null;
		using var observer = new ShellSectionNavigationObserver(section, page => displayed = page);
		observer.Refresh();
		Assert.Same(detail, displayed);
		Assert.Equal(before, creations);
	}

	[Fact]
	public async Task Dispose_StopsNavigationAndContentNotifications()
	{
		var (section, root) = CreateSection();
		Page? displayed = null;
		var observer = new ShellSectionNavigationObserver(section, page => displayed = page);
		observer.Refresh();
		observer.Dispose();
		await section.Push(new ContentPage());
		section.CurrentItem = new ShellContent { Content = new ContentPage() };
		Assert.Same(root, displayed);
	}

	[Fact]
	public void CurrentContentChanged_DisplaysNewRoot()
	{
		var (section, _) = CreateSection();
		Page? displayed = null;
		using var observer = new ShellSectionNavigationObserver(section, page => displayed = page);
		var next = new ContentPage();
		var content = new ShellContent { Content = next };
		section.Items.Add(content);
		section.CurrentItem = content;
		Assert.Same(next, displayed);
	}

	static (TestSection Section, Page Root) CreateSection()
	{
		var root = new ContentPage();
		var section = new TestSection();
		section.Items.Add(new ShellContent { Content = root });
		var item = new ShellItem();
		item.Items.Add(section);
		var shell = new Shell();
		shell.Items.Add(item);
		return (section, root);
	}

	sealed class TestSection : ShellSection
	{
		public Task Push(Page page) => OnPushAsync(page, false);
		public Task<Page> Pop() => OnPopAsync(false);
		public Task PopToRoot() => OnPopToRootAsync(false);
		public void Insert(Page page, Page before) => OnInsertPageBefore(page, before);
		public void Remove(Page page) => OnRemovePage(page);
	}
}
