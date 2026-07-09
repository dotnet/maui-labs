#nullable enable
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Primitives;
using T = CometSamples.JetNews.JetNewsTheme;

namespace CometSamples.JetNews
{
	/// <summary>
	/// JetNews chrome + routing (the gold JetnewsApp): the whole app wrapped in the REAL
	/// ModalNavigationDrawer (Home / Interests destinations), routes swapped through
	/// ContentSwitcher — body-level structure swaps don't reach the retained node tree,
	/// and the Article slot is ONE shell re-bound via <see cref="CurrentPost"/>.
	/// </summary>
	public class JetNewsRoot : View
	{
		/// <summary>0 = Home, 1 = Article, 2 = Interests.</summary>
		public static readonly Signal<int> ScreenIndex = new(0);
		public static readonly Signal<Post> CurrentPost = new(JetNewsData.Posts.HighlightedPost);
		public static readonly Signal<bool> DrawerOpen = new(false);

		public static void OpenPost(string id)
		{
			if (JetNewsData.Posts.AllPosts.FirstOrDefault(p => p.Id == id) is { } post)
			{
				CurrentPost.Value = post;
				ScreenIndex.Value = 1;
			}
		}

		readonly double _topInset;

		public JetNewsRoot(double topInset) => _topInset = topInset;

		[Body]
		View body()
		{
			JetNewsIcons.Register();
			var routes = new ContentSwitcher(ScreenIndex, new View[]
			{
				JetNewsScreens.Home(_topInset, openDrawer: () => DrawerOpen.Value = true),
				JetNewsArticle.Screen(CurrentPost, _topInset, onBack: () => ScreenIndex.Value = 0),
				JetNewsInterests.Screen(_topInset, openDrawer: () => DrawerOpen.Value = true),
			});
			return new Drawer(DrawerOpen, DrawerSheet(), routes);
		}

		// AppDrawer.kt: logo row (pad h28/v24) + Home / Interests drawer items. The row
		// pills are hand-composed to the NavigationDrawerItem metrics (56dp, r28, icon 24 +
		// 12 gap, labelLarge) — promoting to the real widget is backlogged (the Drawer
		// control takes a free-form sheet; NavigationSuite owns the real-item path).
		static View DrawerSheet()
		{
			static Text Tx(string s) => new Text(s).FontFamily("Montserrat");

			static View Item(string icon, string label, int index) =>
				new HStack(spacing: 0f)
				{
					new Icon(icon).IconSize(24)
						.Color(ScreenIndex.Peek() == index ? T.OnSecondaryContainer : T.OnSurfaceVariant)
						.Margin(new Thickness(16, 0, 12, 0)).FlexShrink(0),
					Tx(label).FontSize(14).FontWeight(FontWeight.Medium)
						.Color(ScreenIndex.Peek() == index ? T.OnSecondaryContainer : T.OnSurfaceVariant)
						.VerticalLayoutAlignment(LayoutAlignment.Center),
				}
				.Frame(height: 56)
				.Background(ScreenIndex.Peek() == index ? T.SecondaryContainer : null)
				.CornerRadius(28)
				.Margin(new Thickness(12, 0, 12, 0))
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.OnTap(_ =>
				{
					ScreenIndex.Value = index;
					DrawerOpen.Value = false;
				});

			return new VStack(spacing: 0f)
			{
				new HStack(spacing: 0f)
				{
					Tx("jetnews").FontSize(20).FontWeight(FontWeight.Medium).Color(T.Primary),
				}.Padding(new Thickness(28, 24, 28, 24)),
				Item("home", "Home", 0),
				new HStack().Frame(height: 4),
				Item("list_alt", "Interests", 2),
			}
			.VerticalLayoutAlignment(LayoutAlignment.Fill);
		}
	}
}
