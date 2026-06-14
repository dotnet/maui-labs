#nullable enable
#if ANDROID
using AndroidX.Compose;
using AndroidX.Compose.Runtime;
using AndroidX.Compose.UI.Graphics.Vector;
using Comet.Backend;
using ComposeIcon = AndroidX.Compose.Icon;

namespace Comet.Platform.Compose
{
	/// <summary>Renders a Comet <see cref="Comet.Icon"/> as a real Material <c>Icon</c>
	/// (<c>ImageVector</c> from the <c>Icons</c> set), tinted and sized — not a glyph in a label.</summary>
	sealed class ComposeIconNode : ComposeNode
	{
		readonly MutableState<string> _symbol = new(string.Empty);
		Microsoft.Maui.Graphics.Color? _tint;
		float _size = 24f;
		readonly MutableState<int> _iconVersion = new(0);

		protected override void ApplyControlProperty(PropertyId id, in PropertyValue value)
		{
			if (id == PropertyIds.Icon_Symbol)
				_symbol.Value = value.AsString ?? string.Empty;
			else if (id == PropertyIds.Icon_Tint)
			{
				_tint = value.AsColor;
				_iconVersion.Value++;
			}
			else if (id == PropertyIds.Icon_Size)
				_size = (float)value.AsDouble;
		}

		public override Size Measure(double widthConstraint, double heightConstraint)
			=> new Size(_size, _size);

		public override void Render(IComposer composer)
		{
			_ = _iconVersion.Value;

			// A bundled multicolor vector drawable named "ic_<symbol>" (e.g. ic_jetchat) renders as
			// an Image (painterResource keeps its own colors); everything else is a tinted Material
			// Icon from the ImageVector set.
			var ctx = global::Android.App.Application.Context;
			int resId = ctx.Resources!.GetIdentifier("ic_" + _symbol.Value, "drawable", ctx.PackageName);
			if (resId != 0)
			{
				var image = new AndroidX.Compose.Image(resId, _symbol.Value);
				((ComposableNode)image).Modifier = BuildNodeModifier();
				image.Render(composer);
				return;
			}

			var icon = new ComposeIcon(Resolve(_symbol.Value), _symbol.Value);
			if (_tint is { } t)
				icon.Tint = ToComposeColor(t);
			((ComposableNode)icon).Modifier = BuildNodeModifier();
			icon.Render(composer);
		}

		// Cross-platform symbol name → Material ImageVector. Core set (material-icons-core);
		// a few footer icons fall back to the nearest available until the facade exposes the
		// extended set (mood/alternate_email/photo/duo).
		static ImageVector Resolve(string s) => s switch
		{
			"search" => Icons.Filled.Search,
			"info" => Icons.Outlined.Info,
			"menu" => Icons.Filled.Menu,
			"send" => Icons.AutoMirrored.Default.Send,
			"place" or "location" => Icons.Filled.Place,
			"person" => Icons.Filled.Person,
			"people" => Icons.Filled.AccountCircle,   // jetchat logo stand-in (core set has no "groups")
			"account" => Icons.Filled.AccountCircle,
			"call" or "phone" => Icons.Filled.Call,
			"email" or "mail" => Icons.Filled.Email,
			"close" => Icons.Filled.Close,
			"settings" => Icons.Filled.Settings,
			"share" => Icons.Filled.Share,
			"back" => Icons.AutoMirrored.Default.ArrowBack,
			"add" => Icons.Filled.Add,
			"edit" => Icons.Filled.Edit,
			// Nearest-available stand-ins for Jetchat's extended footer icons:
			"mood" or "emoji" => Icons.Filled.Face,
			"at" => Icons.Filled.Email,
			"photo" or "image" => Icons.Filled.AccountBox,
			"video" or "duo" => Icons.Filled.Call,
			_ => Icons.Filled.Star,
		};
	}
}
#endif
