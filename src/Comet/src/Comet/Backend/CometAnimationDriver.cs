#nullable enable
using System;
using Microsoft.Maui.Animations;

namespace Comet.Backend
{
	/// <summary>
	/// Comet-owned animation manager for the node backends: drives
	/// <see cref="Comet.Animation"/> interpolation from a platform frame ticker
	/// (Choreographer on Android, CADisplayLink on iOS) with no MAUI host in the
	/// loop. <c>View.GetAnimationManager()</c> falls back here when there is no
	/// <c>IMauiContext</c> — before this, <c>view.Animate(...)</c> silently did
	/// nothing on the node backends.
	/// </summary>
	public static class CometAnimationDriver
	{
		/// <summary>The shared manager, or null until a backend installs a ticker.</summary>
		public static IAnimationManager? Shared { get; private set; }

		/// <summary>Installs the platform frame ticker. First caller wins (one ticker per
		/// process); subsequent calls are no-ops so multiple backend roots can call it.</summary>
		public static void Initialize(ICometFrameTicker ticker)
		{
			if (ticker is null)
				throw new ArgumentNullException(nameof(ticker));
			Shared ??= new AnimationManager(new FrameTickerAdapter(ticker));
		}

		/// <summary>Adapts <see cref="ICometFrameTicker"/> to the MAUI animation
		/// library's <see cref="Ticker"/> so the stock <see cref="AnimationManager"/>
		/// (which Comet's Animation types already target) can run unmodified.</summary>
		sealed class FrameTickerAdapter : Ticker
		{
			readonly ICometFrameTicker _ticker;
			IDisposable? _subscription;

			public FrameTickerAdapter(ICometFrameTicker ticker) => _ticker = ticker;

			public override bool IsRunning => _subscription is not null;

			public override void Start() => _subscription ??= _ticker.Subscribe(_ => Fire?.Invoke());

			public override void Stop()
			{
				_subscription?.Dispose();
				_subscription = null;
			}
		}
	}
}
