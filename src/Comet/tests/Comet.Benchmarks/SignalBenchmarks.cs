using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Comet.Reactive;
using ReactiveEffect = Comet.Reactive.Effect;

namespace Comet.Benchmarks;

/// <summary>
/// Benchmarks for the reactive signal system.
/// Targets from docs/state-management-proposal.md §8.3.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SignalBenchmarks
{
	/// <summary>
	/// Initialize the Comet test runtime so ThreadHelper and
	/// ReactiveScheduler work outside a full MAUI host.
	/// </summary>
	[GlobalSetup]
	public void Setup()
	{
		BenchmarkUI.Init();
	}

	// ----------------------------------------------------------------
	// Target: < 50μs on desktop
	// Single signal write → notify subscriber → flush → effect re-run
	// ----------------------------------------------------------------

	Signal<int> _singleSignal;
	ReactiveEffect _singleEffect;

	[IterationSetup(Target = nameof(SingleSignalWrite_SingleTextUpdate))]
	public void SetupSingleSignal()
	{
		_singleSignal = new Signal<int>(0);
		_singleEffect = new ReactiveEffect(() =>
		{
			_ = _singleSignal.Value;
		}, runImmediately: true);
	}

	[Benchmark(Description = "1 Signal write → 1 effect update")]
	public void SingleSignalWrite_SingleTextUpdate()
	{
		_singleSignal.Value = _singleSignal.Peek() + 1;
		ReactiveScheduler.FlushSync();
	}

	// ----------------------------------------------------------------
	// Target: < 200μs on desktop
	// 100 signal writes in the same synchronous frame → single flush
	// ----------------------------------------------------------------

	const int BulkWriteCount = 100;
	Signal<int>[] _bulkSignals;
	ReactiveEffect _bulkEffect;

	[IterationSetup(Target = nameof(HundredSignalWrites_SingleFlush))]
	public void SetupBulkSignals()
	{
		_bulkSignals = new Signal<int>[BulkWriteCount];
		for (var i = 0; i < BulkWriteCount; i++)
			_bulkSignals[i] = new Signal<int>(0);

		_bulkEffect = new ReactiveEffect(() =>
		{
			for (var i = 0; i < _bulkSignals.Length; i++)
				_ = _bulkSignals[i].Value;
		}, runImmediately: true);
	}

	[Benchmark(Description = "100 Signal writes → single flush")]
	public void HundredSignalWrites_SingleFlush()
	{
		for (var i = 0; i < BulkWriteCount; i++)
			_bulkSignals[i].Value = i + 1;

		ReactiveScheduler.FlushSync();
	}

	// ----------------------------------------------------------------
	// Target: < 100μs
	// 1 source signal → 50 Computed bindings, change the source once
	// ----------------------------------------------------------------

	const int ComputedCount = 50;
	Signal<int> _computedSource;
	Computed<string>[] _computedBindings;
	ReactiveEffect[] _computedEffects;

	[IterationSetup(Target = nameof(FiftyComputedBindings_OneSignalChange))]
	public void SetupComputedBindings()
	{
		_computedSource = new Signal<int>(0);
		_computedBindings = new Computed<string>[ComputedCount];
		_computedEffects = new ReactiveEffect[ComputedCount];

		for (var i = 0; i < ComputedCount; i++)
		{
			var idx = i;
			_computedBindings[i] = new Computed<string>(() =>
				$"Item {idx}: {_computedSource.Value}");

			// Force initial evaluation and subscribe an effect so the
			// Computed is fully wired into the reactive graph.
			var computed = _computedBindings[i];
			_computedEffects[i] = new ReactiveEffect(() =>
			{
				_ = computed.Value;
			}, runImmediately: true);
		}
	}

	[Benchmark(Description = "50 Computeds, 1 Signal change → all re-evaluate")]
	public void FiftyComputedBindings_OneSignalChange()
	{
		_computedSource.Value = _computedSource.Peek() + 1;
		ReactiveScheduler.FlushSync();
	}

	// ----------------------------------------------------------------
	// Target: < 500μs
	// SignalList with 1000 items, add 1 item (targeted insert)
	// ----------------------------------------------------------------

	SignalList<string> _largeList;
	ReactiveEffect _listEffect;
	int _listAddCounter;

	[IterationSetup(Target = nameof(SignalList_ThousandItems_AddOne))]
	public void SetupLargeList()
	{
		var items = new List<string>(1000);
		for (var i = 0; i < 1000; i++)
			items.Add($"Item {i}");

		_largeList = new SignalList<string>(items);
		_listAddCounter = 0;

		// Subscribe an effect that reads the list count so the reactive
		// graph is wired and the flush processes the notification.
		_listEffect = new ReactiveEffect(() =>
		{
			_ = _largeList.Count;
		}, runImmediately: true);
	}

	[Benchmark(Description = "1000-item SignalList + 1 Add → targeted insert")]
	public void SignalList_ThousandItems_AddOne()
	{
		_largeList.Add($"New item {_listAddCounter++}");
		ReactiveScheduler.FlushSync();
	}

	// ----------------------------------------------------------------
	// Supplementary: Signal creation + disposal throughput
	// ----------------------------------------------------------------

	[Benchmark(Description = "Create + dispose 1000 Signals")]
	public void CreateAndDispose_ThousandSignals()
	{
		for (var i = 0; i < 1000; i++)
		{
			var signal = new Signal<int>(i);
			signal.Dispose();
		}
	}

	// ----------------------------------------------------------------
	// Supplementary: Computed cache hit (no dependency change)
	// ----------------------------------------------------------------

	Signal<int> _cacheHitSource;
	Computed<int> _cacheHitComputed;

	[IterationSetup(Target = nameof(ComputedCacheHit_NoDepChange))]
	public void SetupCacheHit()
	{
		_cacheHitSource = new Signal<int>(42);
		_cacheHitComputed = new Computed<int>(() => _cacheHitSource.Value * 2);
		_ = _cacheHitComputed.Value; // force initial evaluation
	}

	[Benchmark(Description = "Computed cache hit (100 reads, no dep change)")]
	public int ComputedCacheHit_NoDepChange()
	{
		var sum = 0;
		for (var i = 0; i < 100; i++)
			sum += _cacheHitComputed.Value;
		return sum;
	}

	// ----------------------------------------------------------------
	// Supplementary: SignalList ConsumePendingChanges throughput
	// ----------------------------------------------------------------

	[Benchmark(Description = "SignalList: 100 Adds + ConsumePendingChanges")]
	public int SignalList_BatchAddAndConsume()
	{
		var list = new SignalList<int>();
		for (var i = 0; i < 100; i++)
			list.Add(i);

		var changes = list.ConsumePendingChanges();
		return changes.Count;
	}
}
