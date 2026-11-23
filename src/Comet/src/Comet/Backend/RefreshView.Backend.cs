#nullable enable
using System;
using System.Threading.Tasks;
using Comet.Backend;

namespace Comet
{
	public partial class RefreshView
	{
		Action? _refreshRequested;
		Func<Task>? _refreshRequestedAsync;

		public RefreshView OnRefresh(Action refresh)
		{
			_refreshRequested = refresh;
			_refreshRequestedAsync = null;
			return this;
		}

		public RefreshView OnRefresh(Func<Task> refresh)
		{
			_refreshRequested = null;
			_refreshRequestedAsync = refresh;
			return this;
		}

		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			if (IsRefreshing is not null)
				node.ApplyProperty(PropertyIds.Refresh_IsRefreshing, PropertyValue.From(IsRefreshing.CurrentValue));
		}

		protected internal override void OnBackendEvent(EventId id)
		{
			if (id == EventIds.RefreshEnded)
			{
				IsRefreshing?.Set(false);
				Node?.ApplyProperty(PropertyIds.Refresh_IsRefreshing, PropertyValue.From(false));
				return;
			}

			if (id != EventIds.RefreshRequested)
				return;

			Node?.ApplyProperty(PropertyIds.Refresh_IsRefreshing, PropertyValue.From(true));
			IsRefreshing?.Set(true);
			_refreshRequested?.Invoke();
			if (_refreshRequestedAsync is not null)
				_ = RunRefreshAsync();
		}

		async Task RunRefreshAsync()
		{
			try
			{
				await _refreshRequestedAsync!().ConfigureAwait(false);
			}
			finally
			{
				ThreadHelper.RunOnMainThread(() =>
				{
					IsRefreshing?.Set(false);
					Node?.ApplyProperty(PropertyIds.Refresh_IsRefreshing, PropertyValue.From(false));
				});
			}
		}
	}
}
