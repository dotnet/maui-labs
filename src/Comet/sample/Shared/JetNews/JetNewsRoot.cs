#nullable enable
using System.Linq;
using Comet;
using Comet.Reactive;

namespace CometSamples.JetNews
{
	/// <summary>
	/// Compact-width navigation: Home ↔ Article (the gold's PostKey nav entry).
	/// Routed through <see cref="ContentSwitcher"/> — the swap must live inside the
	/// retained backend node (body-level structure swaps don't reach the node tree);
	/// the Article slot is a single shell that re-binds to <see cref="CurrentPost"/>.
	/// </summary>
	public class JetNewsRoot : View
	{
		/// <summary>0 = Home, 1 = Article.</summary>
		public static readonly Signal<int> ScreenIndex = new(0);
		public static readonly Signal<Post> CurrentPost = new(JetNewsData.Posts.HighlightedPost);

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
		View body() => new ContentSwitcher(ScreenIndex, new View[]
		{
			JetNewsScreens.Home(_topInset),
			JetNewsArticle.Screen(CurrentPost, _topInset, onBack: () => ScreenIndex.Value = 0),
		});
	}
}
