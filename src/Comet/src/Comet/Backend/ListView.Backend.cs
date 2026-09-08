#nullable enable
using Comet.Backend;

namespace Comet
{
	// Backend emission for ListView. The actual rows are pulled lazily by the Compose
	// LazyColumn via IListView; here we just signal (re)composition on data changes.
	public partial class ListView
	{
		protected internal override void ApplyAllSetProperties(ICometBackendNode node)
		{
			base.ApplyAllSetProperties(node);
			// Bump the list node's version so its LazyColumn recomposes against current rows.
			node.ApplyProperty(PropertyIds.List_Version, PropertyValue.From(0));
		}
	}
}
