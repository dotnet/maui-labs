#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Real Material 3 PullToRefreshBox hosting the RefreshView content.</summary>
	sealed class ComposeRefreshNode : ComposeNode
	{
		readonly MutableState<bool> _refreshing = new(false);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Refresh_IsRefreshing)
				_refreshing.Value = value.AsBool;
		}

		public override void Render(IComposer composer)
		{
			var refresh = new PullToRefreshBox(
				isRefreshing: _refreshing.Value,
				onRefresh: () => Sink?.OnEvent(EventIds.RefreshRequested))
			{
				Modifier = BuildNodeModifier(),
			};
			AddChildrenTo(refresh);
			refresh.Render(composer);
		}
	}
}
#endif
