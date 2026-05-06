using CometAllTheLists.Pages;
using TabView = Comet.TabView;

namespace CometAllTheLists;

public class AllTheListsApp : CometApp
{
	public AllTheListsApp()
	{
		Body = CreateRootView;
	}

	public static Comet.View CreateRootView()
	{
		var tabs = TabView();
		tabs.Add(MakeTab(new ShoppingPage(), "Shopping", "tab_shopping.png"));
		tabs.Add(MakeTab(new CollectionViewPage(), "Collections", "tab_collections.png"));
		tabs.Add(MakeTab(new InboxPage(), "Inbox", "tab_inbox.png"));
		tabs.Add(MakeTab(new StreamingServicePage(), "Streaming", "tab_streaming.png"));
		tabs.Add(MakeTab(new AddressBookPage(), "Contacts", "tab_contacts.png"));
		return tabs;
	}

	static NavigationView MakeTab(Comet.View page, string title, string icon)
	{
		var navigation = NavigationView(page.Title(title));
		navigation.TabText(title);
		navigation.TabIcon(icon);
		return navigation;
	}
}

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();

#if DEBUG
		builder.UseCometSampleDebugHost(AllTheListsApp.CreateRootView);
#else
		builder.UseCometApp<AllTheListsApp>();
#endif
#if DEBUG
		builder.EnableSampleRuntimeDebugging();
#endif
		return builder.Build();
	}
}
