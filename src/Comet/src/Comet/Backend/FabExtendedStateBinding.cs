#nullable enable
using System;
using System.ComponentModel;
using Comet.Reactive;

namespace Comet.Backend;

/// <summary>
/// Keeps a retained FAB node subscribed to the current declaration's extended-state
/// signal while owner instances change across reconciliation.
/// </summary>
internal sealed class FabExtendedStateBinding : IDisposable
{
	readonly Action<bool> _apply;
	readonly PropertyChangedEventHandler _signalChanged;
	Fab? _owner;
	Signal<bool>? _signal;
	bool _disposed;

	public FabExtendedStateBinding(Fab owner, Action<bool> apply)
	{
		_apply = apply ?? throw new ArgumentNullException(nameof(apply));
		_signalChanged = (_, _) => PublishCurrent();
		TransferOwner(owner);
	}

	public void TransferOwner(Fab owner)
	{
		if (_disposed)
			return;
		if (owner is null)
			throw new ArgumentNullException(nameof(owner));

		if (_signal is not null)
			_signal.PropertyChanged -= _signalChanged;

		_owner = owner;
		_signal = owner.ExtendedSignal;
		if (_signal is not null)
			_signal.PropertyChanged += _signalChanged;

		ApplyCurrent();
	}

	void PublishCurrent()
	{
		var owner = _owner;
		var signal = _signal;
		var value = signal?.Peek() ?? owner?.Extended ?? false;
		ThreadHelper.RunOnMainThread(() =>
		{
			if (_disposed ||
				!ReferenceEquals(owner, _owner) ||
				!ReferenceEquals(signal, _signal))
				return;
			_apply(value);
		});
	}

	void ApplyCurrent()
	{
		var owner = _owner;
		if (owner is null)
			return;
		_apply(_signal?.Peek() ?? owner.Extended);
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;

		if (_signal is not null)
			_signal.PropertyChanged -= _signalChanged;
		_signal = null;
		_owner = null;
	}
}
