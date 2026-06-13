#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using Comet.Backend;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.Drawer"/> as a modal navigation drawer: the side
	/// panel slides over the content with a tappable scrim, driven by the <c>Drawer_IsOpen</c>
	/// signal. Owns its two children (<see cref="IBackendManagesOwnContent"/>) and lays each out
	/// with the shared Yoga engine (content at full screen, the panel at the sheet width). A
	/// controlled <c>Box</c> overlay so it behaves identically to the SwiftUI sliding panel.</summary>
	sealed class ComposeDrawerNode : ComposeNode, IBackendManagesOwnContent
	{
		static readonly AndroidX.Compose.Color Scrim = AndroidX.Compose.Color.FromArgb(0x52, 0, 0, 0);

		readonly Drawer _drawer;
		readonly BackendContext _context;
		readonly MutableState<bool> _open = new(false);
		ComposeNode? _sideNode, _contentNode;
		float _sheetWidth = 300;

		public ComposeDrawerNode(Drawer drawer, BackendContext context)
		{
			_drawer = drawer;
			_context = context;
		}

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Drawer_IsOpen)
				_open.Value = value.AsBool;
		}

		void EnsureContent()
		{
			if (_contentNode is not null)
				return;

			_sideNode = (ComposeNode)CometBackendBridge.Materialize(_drawer.Side, _context);
			_contentNode = (ComposeNode)CometBackendBridge.Materialize(_drawer.Content, _context);

			var m = global::Android.Content.Res.Resources.System!.DisplayMetrics!;
			double w = m.WidthPixels / ComposeNode.Density;
			double h = m.HeightPixels / ComposeNode.Density;
			_sheetWidth = (float)System.Math.Min(320, w * 0.85);

			CometBackendLayoutEngine.Layout(_drawer.Content, new Microsoft.Maui.Graphics.Size(w, h));
			CometBackendLayoutEngine.Layout(_drawer.Side, new Microsoft.Maui.Graphics.Size(_sheetWidth, h));
		}

		public override void Render(IComposer composer)
		{
			EnsureContent();

			var box = new Box();
			((ComposableNode)box).Modifier = Modifier.Companion.FillMaxSize();
			box.Add(_contentNode!);

			if (_open.Value)
			{
				// Scrim dims + dismisses; the side panel (already sized to the sheet width with its
				// own surface background) sits at the top-left over it.
				var scrim = new Box();
				((ComposableNode)scrim).Modifier = Modifier.Companion
					.FillMaxSize()
					.Background(Scrim)
					.Clickable(() => Sink?.OnEvent(EventIds.DrawerClosed));
				box.Add(scrim);
				box.Add(_sideNode!);
			}

			box.Render(composer);
		}
	}
}
#endif
