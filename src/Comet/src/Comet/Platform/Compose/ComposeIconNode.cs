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

		// Brand logos that ship their own colors — rendered as an Image (untinted) so
		// painterResource preserves them. Every other bundled "ic_<symbol>" is tinted.
		static readonly System.Collections.Generic.HashSet<string> MulticolorAssets = new() { "jetchat" };

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

			// Honor IconSize even when the engine hasn't given this node a frame (e.g. an icon inside a
			// native control's slot — a FAB — which lays its own content out). With a frame, the size
			// already comes from the frame; without one, pin it here so the glyph isn't the default 24dp.
			Modifier IconModifier()
			{
				var m = BuildNodeModifier() ?? Modifier.Companion;
				return HasFrame ? m : m.Size(new Dp(_size), new Dp(_size));
			}

			// A bundled vector drawable named "ic_<symbol>" wins over the built-in ImageVector set,
			// so apps can ship exact assets (e.g. Jetchat's own footer icons). Multicolor brand
			// logos (jetchat) render as an Image so painterResource keeps their own colors;
			// everything else renders through the tinted Material Icon (painter overload), so the
			// requested .Color() recolors the glyph just like a built-in icon.
			var ctx = global::Android.App.Application.Context;
			int resId = ctx.Resources!.GetIdentifier("ic_" + _symbol.Value, "drawable", ctx.PackageName);
			if (resId != 0)
			{
				// A multicolor brand logo renders as an Image to keep its own colors — UNLESS an
				// explicit .Color() asks for a tint, in which case it routes through the tinted Icon
				// path and renders monochrome (e.g. the drawer's chat-row logo, tinted onSurfaceVariant).
				if (MulticolorAssets.Contains(_symbol.Value) && _tint is null)
				{
					var image = new AndroidX.Compose.Image(resId, _symbol.Value);
					((ComposableNode)image).Modifier = IconModifier();
					image.Render(composer);
					return;
				}

				var bundled = new ComposeIcon(resId, _symbol.Value);
				if (_tint is { } bt)
					bundled.Tint = ToComposeColor(bt);
				((ComposableNode)bundled).Modifier = IconModifier();
				bundled.Render(composer);
				return;
			}

			var icon = new ComposeIcon(Resolve(_symbol.Value), _symbol.Value);
			if (_tint is { } t)
				icon.Tint = ToComposeColor(t);
			((ComposableNode)icon).Modifier = IconModifier();
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
			"arrow_down" or "arrow_downward" or "expand_more" => Icons.Filled.KeyboardArrowDown,
			"add" => Icons.Filled.Add,
			"edit" => Icons.Filled.Edit,
			// Nearest-available stand-ins for Jetchat's extended footer icons:
			"mood" or "emoji" => Icons.Filled.Face,
			"at" => Icons.Filled.Email,
			"photo" or "image" => Icons.Filled.AccountBox,
			"video" or "duo" => Icons.Filled.Call,
			"mic" or "microphone" => Icons.Filled.Phone,   // core set has no Mic glyph (stand-in)
			_ => Icons.Filled.Star,
		};
	}
}
#endif
