using System;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Comet.Reactive;
using Microsoft.Maui.HotReload;

namespace Comet.Benchmarks;

/// <summary>
/// Hot reload benchmarks.
/// Target from §8.3: View with 10 Signals hot-reloaded, state preserved, &lt; 100ms.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class HotReloadBenchmarks
{
	[GlobalSetup]
	public void Setup()
	{
		BenchmarkUI.Init();
	}

	// ----------------------------------------------------------------
	// Target: < 100ms total
	// Hot reload of a view with 10 Signal fields — state preserved
	// ----------------------------------------------------------------

	class TenSignalView : View
	{
		public readonly Signal<int> S0 = new(0);
		public readonly Signal<int> S1 = new(1);
		public readonly Signal<int> S2 = new(2);
		public readonly Signal<int> S3 = new(3);
		public readonly Signal<int> S4 = new(4);
		public readonly Signal<int> S5 = new(5);
		public readonly Signal<int> S6 = new(6);
		public readonly Signal<int> S7 = new(7);
		public readonly Signal<int> S8 = new(8);
		public readonly Signal<int> S9 = new(9);

		[Body]
		View body() => new VStack
		{
			new Text($"{S0.Value} {S1.Value} {S2.Value} {S3.Value} {S4.Value}"),
			new Text($"{S5.Value} {S6.Value} {S7.Value} {S8.Value} {S9.Value}"),
		};
	}

	TenSignalView _oldView;
	TenSignalView _newView;

	[IterationSetup(Target = nameof(HotReload_TenSignals_StateTransfer))]
	public void SetupHotReload()
	{
		_oldView = new TenSignalView();
		// Set distinctive values on the old view's signals
		_oldView.S0.Value = 100;
		_oldView.S5.Value = 500;
		_oldView.S9.Value = 900;

		_newView = new TenSignalView();
	}

	[Benchmark(Description = "Hot reload: 10-Signal view state transfer")]
	public void HotReload_TenSignals_StateTransfer()
	{
		// TransferHotReloadStateTo is internal; use the IHotReloadableView interface
		((IHotReloadableView)_oldView).TransferState(_newView);
	}

	// ----------------------------------------------------------------
	// Supplementary: Measure reflection cost for varying signal counts
	// ----------------------------------------------------------------

	class FiftySignalView : View
	{
		public readonly Signal<int> S00 = new(0);
		public readonly Signal<int> S01 = new(1);
		public readonly Signal<int> S02 = new(2);
		public readonly Signal<int> S03 = new(3);
		public readonly Signal<int> S04 = new(4);
		public readonly Signal<int> S05 = new(5);
		public readonly Signal<int> S06 = new(6);
		public readonly Signal<int> S07 = new(7);
		public readonly Signal<int> S08 = new(8);
		public readonly Signal<int> S09 = new(9);
		public readonly Signal<int> S10 = new(10);
		public readonly Signal<int> S11 = new(11);
		public readonly Signal<int> S12 = new(12);
		public readonly Signal<int> S13 = new(13);
		public readonly Signal<int> S14 = new(14);
		public readonly Signal<int> S15 = new(15);
		public readonly Signal<int> S16 = new(16);
		public readonly Signal<int> S17 = new(17);
		public readonly Signal<int> S18 = new(18);
		public readonly Signal<int> S19 = new(19);
		public readonly Signal<string> S20 = new("a");
		public readonly Signal<string> S21 = new("b");
		public readonly Signal<string> S22 = new("c");
		public readonly Signal<string> S23 = new("d");
		public readonly Signal<string> S24 = new("e");
		public readonly Signal<string> S25 = new("f");
		public readonly Signal<string> S26 = new("g");
		public readonly Signal<string> S27 = new("h");
		public readonly Signal<string> S28 = new("i");
		public readonly Signal<string> S29 = new("j");
		public readonly Signal<bool> S30 = new(true);
		public readonly Signal<bool> S31 = new(false);
		public readonly Signal<bool> S32 = new(true);
		public readonly Signal<bool> S33 = new(false);
		public readonly Signal<bool> S34 = new(true);
		public readonly Signal<bool> S35 = new(false);
		public readonly Signal<bool> S36 = new(true);
		public readonly Signal<bool> S37 = new(false);
		public readonly Signal<bool> S38 = new(true);
		public readonly Signal<bool> S39 = new(false);
		public readonly Signal<double> S40 = new(1.0);
		public readonly Signal<double> S41 = new(2.0);
		public readonly Signal<double> S42 = new(3.0);
		public readonly Signal<double> S43 = new(4.0);
		public readonly Signal<double> S44 = new(5.0);
		public readonly Signal<double> S45 = new(6.0);
		public readonly Signal<double> S46 = new(7.0);
		public readonly Signal<double> S47 = new(8.0);
		public readonly Signal<double> S48 = new(9.0);
		public readonly Signal<double> S49 = new(10.0);

		[Body]
		View body() => new Text("50 signals");
	}

	FiftySignalView _oldFifty;
	FiftySignalView _newFifty;

	[IterationSetup(Target = nameof(HotReload_FiftySignals_StateTransfer))]
	public void SetupFiftySignalReload()
	{
		_oldFifty = new FiftySignalView();
		_newFifty = new FiftySignalView();
	}

	[Benchmark(Description = "Hot reload: 50-Signal view state transfer")]
	public void HotReload_FiftySignals_StateTransfer()
	{
		((IHotReloadableView)_oldFifty).TransferState(_newFifty);
	}
}
