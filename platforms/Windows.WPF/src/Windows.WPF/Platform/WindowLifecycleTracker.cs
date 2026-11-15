using System;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Forwards a WPF window's lifecycle to the public <see cref="IWindow"/> methods, firing each
	/// transition exactly once however many WPF events report it.
	/// </summary>
	/// <remarks>
	/// <list type="bullet">
	/// <item><see cref="IWindow.Created"/> once, before the window is shown.</item>
	/// <item><see cref="IWindow.Activated"/> / <see cref="IWindow.Deactivated"/> on focus changes.</item>
	/// <item><see cref="IWindow.Stopped"/> then <see cref="IWindow.Backgrounding"/> when the window is
	/// minimized or closed, and <see cref="IWindow.Resumed"/> when it comes back from being stopped.</item>
	/// <item><see cref="IWindow.Destroying"/> once when the window has closed, after it has been
	/// deactivated and stopped.</item>
	/// </list>
	/// </remarks>
	internal sealed class WindowLifecycleTracker
	{
		readonly IWindow _window;
		bool _isCreated;
		bool _isActive;
		bool _isStopped;
		bool _isDestroyed;

		public WindowLifecycleTracker(IWindow window)
		{
			_window = window ?? throw new ArgumentNullException(nameof(window));
		}

		public IWindow Window => _window;

		public void Create()
		{
			if (_isCreated || _isDestroyed)
				return;
			_isCreated = true;
			_window.Created();
		}

		public void Activate()
		{
			if (_isActive || _isDestroyed)
				return;
			// Activating a minimized window restores it.
			Resume();
			_isActive = true;
			_window.Activated();
		}

		public void Deactivate()
		{
			if (!_isActive || _isDestroyed)
				return;
			_isActive = false;
			_window.Deactivated();
		}

		public void Stop()
		{
			if (_isStopped || _isDestroyed)
				return;
			_isStopped = true;
			_window.Stopped();
			_window.Backgrounding(new PersistedState());
		}

		public void Resume()
		{
			if (!_isStopped || _isDestroyed)
				return;
			_isStopped = false;
			_window.Resumed();
		}

		public void Destroy()
		{
			if (_isDestroyed)
				return;
			Deactivate();
			Stop();
			_isDestroyed = true;
			_window.Destroying();
		}
	}
}
