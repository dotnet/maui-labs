#nullable enable
using System;
using System.Threading;

namespace Comet.Backend;

/// <summary>
/// Routes native scroll-state callbacks to the logical <see cref="ScrollView"/> that
/// currently owns a retained backend node.
/// </summary>
internal sealed class ScrollOwnerStateBridge : IDisposable
{
	ScrollView? _owner;

	public ScrollOwnerStateBridge(ScrollView owner)
		=> _owner = owner ?? throw new ArgumentNullException(nameof(owner));

	public void TransferOwner(ScrollView owner)
		=> Volatile.Write(ref _owner, owner ?? throw new ArgumentNullException(nameof(owner)));

	public void Publish(double offset, bool atTop)
	{
		ThreadHelper.RunOnMainThread(() =>
		{
			var owner = Volatile.Read(ref _owner);
			if (owner is null)
				return;

			owner.ScrollOffset.Value = offset;
			owner.AtTop.Value = atTop;
		});
	}

	public void Dispose()
		=> Volatile.Write(ref _owner, null);
}
