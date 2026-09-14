using System;
using BenchmarkDotNet.Attributes;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace Comet.Benchmarks;

/// <summary>
/// Node-backend patch-stream benchmarks (the plan's Phase-0/4 verification item):
/// measures the steady-state cost and allocations of the diff→backend contract —
/// materialization, reactive property patches, and reload-driven node transfer —
/// against a recording no-op node, so the numbers are pure Comet-side overhead
/// (no Compose/SwiftUI interop in the loop).
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class BackendMutationBenchmarks
{
	/// <summary>Minimal ICometBackendNode: counts patches, allocates nothing per call.</summary>
	sealed class NullBackendNode : ICometBackendNode
	{
		public int ApplyCount;
		public void ApplyProperty(PropertyId id, in PropertyValue value) => ApplyCount++;
		public void InsertChild(int index, ICometBackendNode child) { }
		public void RemoveChildAt(int index) { }
		public void MoveChild(int fromIndex, int toIndex) { }
		public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;
		public void Arrange(Rect frame) { }
		public void SetEventSink(ICometEventSink sink) { }
		public void Dispose() { }
	}

	sealed class NullServiceProvider : IServiceProvider
	{
		public object GetService(Type serviceType) => null;
	}

	class LabelHostView : View
	{
		public string Label = "before";

		[Body]
		View body() => new VStack
		{
			new Text(Label),
			new Text("static one"),
			new Text("static two"),
			new Button("Tap", () => { }),
		};
	}

	BackendContext _context;
	Signal<int> _counter;
	View _boundRoot;
	LabelHostView _reloadHost;
	int _label;

	[GlobalSetup]
	public void Setup()
	{
		BenchmarkUI.Init();
		ThreadHelper.SetFireOnMainThread(a => a?.Invoke());
		_context = new BackendContext(new NullServiceProvider());

		// Reactive-patch scenario: one bound Text among static siblings.
		_counter = new Signal<int>(0);
		_boundRoot = new VStack
		{
			new Text(() => $"Count: {_counter.Value}"),
			new Text("static one"),
			new Text("static two"),
		};
		CometBackendBridge.Materialize(_boundRoot, _ => new NullBackendNode(), _context);

		// Reload scenario: a [Body] host whose rebuild diffs onto retained nodes.
		_reloadHost = new LabelHostView();
		CometBackendBridge.Materialize(_reloadHost, _ => new NullBackendNode(), _context);
	}

	// ----------------------------------------------------------------
	// Steady state: a Signal change flushing one property patch to the
	// retained node (the per-frame hot loop of the reactive UI).
	// ----------------------------------------------------------------
	[Benchmark]
	public void SignalChange_PatchesNode()
	{
		_counter.Value++;
		ReactiveScheduler.FlushSync();
	}

	// ----------------------------------------------------------------
	// Materialize a small (5-node) tree: view→node bridge cost incl.
	// set-only property emission.
	// ----------------------------------------------------------------
	[Benchmark]
	public object Materialize_SmallTree()
	{
		var root = new VStack
		{
			new Text("one"),
			new Text("two"),
			new Button("Tap", () => { }),
			new HStack { new Text("nested") },
		};
		return CometBackendBridge.Materialize(root, _ => new NullBackendNode(), _context);
	}

	// ----------------------------------------------------------------
	// Reload: rebuild the [Body] tree, diff against the old one, and
	// transfer the retained nodes (the hot-reload / Reload() path).
	// ----------------------------------------------------------------
	[Benchmark]
	public void Reload_DiffsOntoRetainedNodes()
	{
		_reloadHost.Label = (++_label & 1) == 0 ? "even" : "odd";
		_reloadHost.Reload();
	}
}
