#nullable enable
#if IOS
using System;
using Foundation;
using UIKit;

namespace Comet.Platform.SwiftUI
{
	/// <summary>
	/// Tracks how much the software keyboard overlaps the screen so the absolute-positioned
	/// SwiftUI backend can shrink its laid-out height (SwiftUI's automatic keyboard avoidance
	/// doesn't apply to our offset-positioned nodes). Own-content nodes that lay out a full-height
	/// screen (the navigation screen) read <see cref="Inset"/> and re-run their layout on
	/// <see cref="Changed"/>, so the footer rises above the keyboard instead of hiding behind it.
	/// </summary>
	static class CometSwiftUIKeyboard
	{
		static NSObject? _change, _hide;

		/// <summary>Points of the screen currently covered by the keyboard (0 when hidden).</summary>
		public static nfloat Inset { get; private set; }

		/// <summary>Raised (on the main thread) whenever <see cref="Inset"/> changes.</summary>
		public static event Action? Changed;

		/// <summary>Idempotently starts observing keyboard frame changes.</summary>
		public static void EnsureStarted()
		{
			if (_change is not null)
				return;

			_change = UIKeyboard.Notifications.ObserveWillChangeFrame((_, e) =>
			{
				var screen = UIScreen.MainScreen.Bounds;
				SetInset((nfloat)Math.Max(0, screen.Bottom - e.FrameEnd.Top));
			});
			_hide = UIKeyboard.Notifications.ObserveWillHide((_, _) => SetInset(0));
		}

		static void SetInset(nfloat value)
		{
			if (Math.Abs(value - Inset) < 0.5)
				return;
			Inset = value;
			Changed?.Invoke();
		}
	}
}
#endif
