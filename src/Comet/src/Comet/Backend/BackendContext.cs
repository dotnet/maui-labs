#nullable enable
using System;

namespace Comet.Backend
{
	/// <summary>
	/// A per-frame tick source the backend supplies so Comet's animation engine can
	/// drive interpolation without taking a dependency on MAUI's IAnimationManager.
	/// Implemented per platform (Choreographer on Android, CADisplayLink on iOS).
	/// </summary>
	public interface ICometFrameTicker
	{
		/// <summary>Subscribes a per-frame callback; returns a token whose disposal unsubscribes.</summary>
		IDisposable Subscribe(Action<TimeSpan> onFrame);
	}

	/// <summary>
	/// Ambient services a backend hands to the nodes it creates — the Comet-owned
	/// replacement for <c>IMauiContext</c>. Carries the DI container and the platform
	/// frame ticker; platform backends may subclass to add native handles (e.g. an
	/// Android <c>Context</c> or the root composition).
	/// </summary>
	public class BackendContext
	{
		public BackendContext(IServiceProvider services, ICometFrameTicker? ticker = null)
		{
			Services = services ?? throw new ArgumentNullException(nameof(services));
			Ticker = ticker;
		}

		/// <summary>The application service provider (replaces <c>IMauiContext.Services</c>).</summary>
		public IServiceProvider Services { get; }

		/// <summary>The per-platform animation tick source, if the backend provides one.</summary>
		public ICometFrameTicker? Ticker { get; }
	}

	/// <summary>
	/// A node that can produce a backend node for itself. Comet's <c>View</c>-derived
	/// controls implement this via their generated platform partial; selecting the
	/// concrete node by virtual dispatch (rather than a reflective registry) is what
	/// lets the trimmer drop unused controls and their backend nodes together.
	/// </summary>
	public interface ICometBackendNodeFactory
	{
		ICometBackendNode CreateBackendNode(BackendContext context);
	}
}
