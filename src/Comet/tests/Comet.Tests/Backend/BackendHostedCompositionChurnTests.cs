#nullable enable
using System;
using System.Collections.Generic;
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Host-side reproduction harness for the iOS hosted-composition twins
	/// (<c>SwiftUIHostedCompositionNode</c> and subclasses): fake own-content nodes that copy
	/// the twins' Refresh/Relayout/Arrange semantics — rebuild wrapper views around PERSISTENT
	/// slot views, re-materialize the subtree, re-layout — so the Reply compact detail-swap
	/// crash (stack overflow / runaway during <c>Layout(Detail)</c> after
	/// <c>IsDetailOpen=true</c>, with suite/switcher refresh churn) can be reproduced and
	/// fixed off-device. A re-entrancy guard turns unbounded recursion into a readable
	/// assertion failure instead of a process kill.
	/// </summary>
	public class BackendHostedCompositionChurnTests
	{
		static BackendHostedCompositionChurnTests() => ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}

		static ICometBackendNode Factory(View v) => v switch
		{
			NavigationSuite suite => new FakeSuiteNode(suite, Factory, Ctx),
			ContentSwitcher switcher => new FakeSwitcherNode(switcher, Factory, Ctx),
			ListDetail listDetail => new FakeListDetailNode(listDetail, Factory, Ctx),
			IListView list => new FakeListNode(list, Factory, Ctx),
			_ => new FakeBackendNode(v.GetType().Name),
		};

		/// <summary>Mirror of SwiftUIListNode: owns its rows, materializes each row's template
		/// view on List_Version patches, lays rows out to the arranged width.</summary>
		sealed class FakeListNode : FakeBackendNode, IBackendManagesOwnContent
		{
			readonly IListView _list;
			readonly CometNodeFactory _factory;
			readonly BackendContext _context;
			readonly List<View> _rowViews = new();
			double _width;

			public FakeListNode(IListView list, CometNodeFactory factory, BackendContext context)
				: base("list")
			{
				_list = list;
				_factory = factory;
				_context = context;
			}

			public override void ApplyProperty(PropertyId id, in PropertyValue value)
			{
				base.ApplyProperty(id, value);
				if (id == PropertyIds.List_Version)
					Rebuild();
			}

			void Rebuild()
			{
				using var hold = ReactiveScheduler.HoldFlushes();
				if (_list is View listView)
					Comet.DevTools.CometDevRegistry.UnregisterSubtree(listView, includeRoot: false);
				_rowViews.Clear();
				int count = _list.Sections() > 0 ? _list.Rows(0) : 0;
				for (int i = 0; i < count; i++)
				{
					var view = _list.ViewFor(0, i);
					CometBackendBridge.Materialize(view, _factory, _context);
					_rowViews.Add(view);
					LayoutRow(view);
				}
			}

			void LayoutRow(View row)
			{
				if (_width > 0)
					CometBackendLayoutEngine.LayoutContent(row, _width);
			}

			public override void Arrange(Rect frame)
			{
				base.Arrange(frame);
				if (frame.Width > 0 && Math.Abs(frame.Width - _width) > 0.5)
				{
					_width = frame.Width;
					foreach (var row in _rowViews)
						LayoutRow(row);
				}
			}
		}

		// ---------------------------------------------------------------- fake twins

		/// <summary>Mirror of SwiftUIHostedCompositionNode minus the shim: one hosted subtree,
		/// swapped on state patches, re-laid-out after every reactive flush.</summary>
		abstract class FakeHostedNode : FakeBackendNode, IBackendManagesOwnContent
		{
			protected readonly BackendContext Context;
			protected readonly CometNodeFactory NodeFactory;
			View? _shown;
			Size _frame;

			public int RefreshCount { get; private set; }
			public static int Depth;
			public static int MaxDepth;
			public static int TotalRefreshes;
			public static readonly List<FakeHostedNode> Instances = new();

			protected FakeHostedNode(CometNodeFactory factory, BackendContext context)
				: base("hosted")
			{
				NodeFactory = factory;
				Context = context;
				Instances.Add(this);
				ReactiveScheduler.AfterFlush += Relayout;   // matches the iOS twin (unhook in Dispose)
			}

			protected abstract View BuildContent();

			List<ICometBackendNode>? _generation;

			public void Refresh()
			{
				RefreshCount++;
				TotalRefreshes++;
				// Matches the iOS twin: the swap is atomic w.r.t. reactive flushes, and the
				// previous node generation is disposed (releases static hooks).
				using var hold = ReactiveScheduler.HoldFlushes();
				Enter();
				try
				{
					if (_shown is { } prev)
					{
						Comet.DevTools.CometDevRegistry.UnregisterSubtree(prev, includeRoot: true);
						_shown = null;
					}
					if (_generation is { } stale)
					{
						_generation = null;
						foreach (var n in stale)
							n.Dispose();
					}
					var view = BuildContent();
					var generation = new List<ICometBackendNode>();
					using (CometBackendBridge.CollectNodes(generation))
						CometBackendBridge.Materialize(view, NodeFactory, Context);
					_generation = generation;
					_shown = view;
					Relayout();
				}
				finally { Exit(); }
			}

			protected void Relayout()
			{
				if (_shown is not { } v)
					return;
				Enter();
				try
				{
					var size = _frame.Width > 0 ? _frame : new Size(402, 874);
					CometBackendLayoutEngine.Layout(v, size);
				}
				finally { Exit(); }
			}

			public static string? DeepStack;

			static void Enter()
			{
				Depth++;
				if (Depth > MaxDepth) MaxDepth = Depth;
				if (Depth == 20 && DeepStack is null)
					DeepStack = Environment.StackTrace;
				if (Depth > 64)
					throw new InvalidOperationException(
						"hosted-composition runaway: Refresh/Relayout re-entrancy exceeded 64");
			}
			static void Exit() => Depth--;

			public override void ApplyProperty(PropertyId id, in PropertyValue value) => OnPatch(id, value);
			protected abstract void OnPatch(PropertyId id, in PropertyValue value);

			public override Size Measure(double widthConstraint, double heightConstraint)
			{
				double w = double.IsFinite(widthConstraint) && widthConstraint > 0 ? widthConstraint : 402;
				double h = double.IsFinite(heightConstraint) && heightConstraint > 0 ? heightConstraint : 874;
				return new Size(w, h);
			}

			public override void Arrange(Rect frame)
			{
				_frame = new Size(frame.Width, frame.Height);
				if (_shown is null)
					Refresh();
				else
					Relayout();
			}

			public override void Dispose()
			{
				base.Dispose();
				ReactiveScheduler.AfterFlush -= Relayout;
				if (_generation is { } nodes)
				{
					_generation = null;
					foreach (var n in nodes)
						n.Dispose();
				}
			}
		}

		sealed class FakeSuiteNode : FakeHostedNode
		{
			readonly NavigationSuite _suite;
			int _selected;
			bool _applied;

			public FakeSuiteNode(NavigationSuite suite, CometNodeFactory f, BackendContext c) : base(f, c)
				=> _suite = suite;

			protected override View BuildContent()
			{
				// Compact bottom-bar variant, matching SwiftUINavigationSuiteNode: fresh wrapper
				// stacks each build; the PERSISTENT Content and item IconViews are re-added and
				// re-modified (FlexGrow etc.) every time.
				var row = new HStack(spacing: 0f);
				for (int i = 0; i < _suite.Items.Count; i++)
				{
					int index = i;
					row.Add(new VStack(spacing: 0f) { _suite.Items[i].IconView }
						.FlexGrow(1).FlexBasis(0)
						.OnTap(_ => _suite.SelectItem(index)));
				}
				return new VStack(spacing: 0f)
				{
					new HStack().Frame(height: 59f).FlexShrink(0),
					_suite.Content.FlexGrow(1).FlexBasis(0),
					new VStack(spacing: 0f) { row.Frame(height: 64f), new HStack().Frame(height: 34f) }
						.FlexShrink(0),
				};
			}

			protected override void OnPatch(PropertyId id, in PropertyValue value)
			{
				if (id != PropertyIds.Nav_SelectedIndex)
					return;
				if (_applied && value.AsInt == _selected)
					return;
				_applied = true;
				_selected = value.AsInt;
				Refresh();
			}
		}

		sealed class FakeSwitcherNode : FakeHostedNode
		{
			readonly ContentSwitcher _switcher;
			int _index;
			bool _applied;

			public FakeSwitcherNode(ContentSwitcher switcher, CometNodeFactory f, BackendContext c) : base(f, c)
				=> _switcher = switcher;

			protected override View BuildContent()
				=> _index >= 0 && _index < _switcher.Views.Count ? _switcher.Views[_index] : new VStack();

			protected override void OnPatch(PropertyId id, in PropertyValue value)
			{
				if (id != PropertyIds.ContentSwitcher_Index)
					return;
				if (_applied && value.AsInt == _index)
					return;
				_applied = true;
				_index = value.AsInt;
				Refresh();
			}
		}

		sealed class FakeListDetailNode : FakeHostedNode
		{
			readonly ListDetail _listDetail;
			bool _open;
			bool _applied;

			public FakeListDetailNode(ListDetail listDetail, CometNodeFactory f, BackendContext c) : base(f, c)
				=> _listDetail = listDetail;

			protected override View BuildContent()
				=> _open ? _listDetail.Detail : _listDetail.List;   // compact swap

			protected override void OnPatch(PropertyId id, in PropertyValue value)
			{
				if (id != PropertyIds.ListDetail_IsDetailOpen)
					return;
				if (_applied && value.AsBool == _open)
					return;
				_applied = true;
				_open = value.AsBool;
				Refresh();
			}
		}

		// ---------------------------------------------------------------- the repro

		/// <summary>The ReplyProbeRoot analog: a [Body] view whose body builds a FRESH suite —
		/// fresh ContentSwitcher, fresh ListDetail, fresh panes — around the same persistent
		/// static-style signals, exactly like ReplyScreens.Inbox(). A body rebuild therefore
		/// re-hooks the signals from new control instances.</summary>
		sealed class ProbeRoot : View
		{
			readonly MiniReply _app;
			public ProbeRoot(MiniReply app) => _app = app;

			[Body]
			View body() => _app.BuildSuite();
		}

		/// <summary>A Reply-shaped mini app: ListView panes, reactive Text closures reading the
		/// opened-email signal, and the ReloadData hook — the pieces the real ReplyScreens uses.</summary>
		sealed class MiniReply
		{
			public readonly Signal<bool> DetailOpen = new(false);
			public readonly Signal<int> OpenedEmailId = new(0);
			public readonly Signal<int> Selected = new(0);
			ListView<int>? _inboxList;

			public MiniReply()
				// Static-ctor analog from ReplyScreens: opened highlight moves via ReloadData.
				=> OpenedEmailId.PropertyChanged += (_, __) => _inboxList?.ReloadData();

			public NavigationSuite BuildSuite()
			{
				var inbox = new VStack(spacing: 0f)
				{
					new ListDetail(DetailOpen, InboxList(), EmailDetail()).FlexGrow(1).FlexBasis(0),
				};
				var articles = new VStack { new Text("Screen under construction") };
				var switcher = new ContentSwitcher(Selected, new View[] { inbox, articles });
				return new NavigationSuite(Selected,
					new[]
					{
						new NavigationItem(new Text("inbox")),
						new NavigationItem(new Text("article")),
					},
					switcher);
			}

			View InboxList()
			{
				var list = new ListView<int>(() => new List<int> { 1, 2, 3 })
				{
					ViewFor = i => Row(i),
				};
				_inboxList = list;
				return new VStack(spacing: 0f) { list.FlexGrow(1).FlexBasis(0) }
					.Padding(new Microsoft.Maui.Thickness(16, 4, 16, 4));
			}

			View Row(int i) => new VStack(spacing: 2f)
			{
				new Text($"Email {i}"),
				new Text("Preview text"),
			}
			.Background(i == OpenedEmailId.Value ? Colors.Red : Colors.Gray)
			.OnTap(_ =>
			{
				// The real EmailListItem tap: highlight signal first, then the pane swap.
				OpenedEmailId.Value = i;
				DetailOpen.Value = true;
			});

			View EmailDetail()
			{
				var stack = new VStack(spacing: 0f)
				{
					new HStack(spacing: 0f)
					{
						new Text("back").OnTap(_ => DetailOpen.Value = false).FlexShrink(0),
						new VStack(spacing: 2f)
						{
							new Text(() => $"Subject of {OpenedEmailId.Value}"),
							new Text(() => $"{OpenedEmailId.Value} Messages"),
						}.FlexGrow(1).FlexBasis(0),
					},
				};
				// The items closure reads the signal, like the real `() => Opened().Threads`;
				// the row template carries a modifier (an environment WRITE during lazy row
				// materialization) like the real ThreadItem's .FontSize — the trigger of the
				// on-device stack overflow.
				var threads = new ListView<int>(() => new List<int> { OpenedEmailId.Value, 2 })
				{
					ViewFor = t => new Text(() => $"thread {t} of {OpenedEmailId.Value}").FontSize(12),
				};
				stack.Add(threads.FlexGrow(1).FlexBasis(0));
				return stack;
			}
		}

		[Fact]
		public void CompactDetailSwap_AfterHostedChurn_StaysBounded()
		{
			FakeHostedNode.Depth = 0;
			FakeHostedNode.MaxDepth = 0;
			FakeHostedNode.TotalRefreshes = 0;
			FakeHostedNode.Instances.Clear();

			var app = new MiniReply();
			var probe = new ProbeRoot(app);
			CometBackendBridge.Materialize(probe, Factory, Ctx);

			// SwiftUIBackendRoot analog: layout on mount + after every reactive flush,
			// re-resolving BuiltView each pass (a body rebuild swaps it).
			void RunLayout()
			{
				var layoutRoot = probe.BuiltView ?? probe;
				CometBackendLayoutEngine.Layout(layoutRoot, new Size(402, 874));
			}
			ReactiveScheduler.AfterFlush += RunLayout;
			try
			{
				RunLayout();

				// Device-observed churn: the suite node refreshes again when the window
				// metrics land (size + safe area publish separately, then again on rotation
				// etc.). Each suite refresh re-materializes the persistent switcher/ListDetail
				// controls into NEW node generations; the old generations stay
				// AfterFlush-subscribed (never disposed) — the state on device.
				var suiteNode = (FakeSuiteNode)FakeHostedNode.Instances[0];
				for (int i = 0; i < 3; i++)
					suiteNode.Refresh();

				// The tap (real EmailListItem ordering): highlight first — fires ReloadData
				// and dirties whatever tracked the signal — then the pane swap. On-device
				// this stack-overflows in the environment walk during Layout(Detail).
				app.OpenedEmailId.Value = 2;
				app.DetailOpen.Value = true;

				// Back, then open another email; repeat to let per-rebuild handler stacking
				// (each fresh ListDetail hooks the same persistent signal) compound.
				for (int i = 3; i < 8; i++)
				{
					app.DetailOpen.Value = false;
					app.OpenedEmailId.Value = i;
					app.DetailOpen.Value = true;
				}

				var byType = new Dictionary<string, (int nodes, int refreshes)>();
				foreach (var n in FakeHostedNode.Instances)
				{
					var key = n.GetType().Name;
					var cur = byType.TryGetValue(key, out var c) ? c : (0, 0);
					byType[key] = (cur.Item1 + 1, cur.Item2 + n.RefreshCount);
				}
				var diag = $"maxDepth={FakeHostedNode.MaxDepth} totalRefreshes={FakeHostedNode.TotalRefreshes} "
					+ string.Join(" ", System.Linq.Enumerable.Select(byType, kv => $"{kv.Key}: n={kv.Value.nodes} r={kv.Value.refreshes}"));

				Assert.True(FakeHostedNode.MaxDepth <= 16,
					$"Refresh/Relayout re-entrancy too deep: {diag}\n{FakeHostedNode.DeepStack}");
				// Bound the churn itself: 3 suite refreshes + 13 detail flips currently cost
				// 9 hosted-node generations / 24 refreshes (linear in interactions). Headroom
				// so incidental changes don't flake; a runaway blows straight past this.
				Assert.True(FakeHostedNode.Instances.Count <= 20 && FakeHostedNode.TotalRefreshes <= 60,
					$"hosted-node churn runaway: {diag}");

				// Stale generations must be DISPOSED (static hooks released): only the live
				// tree's hosted nodes — one suite, one switcher, one ListDetail — may remain.
				var alive = FakeHostedNode.Instances.FindAll(n => !n.Disposed).Count;
				Assert.True(alive <= 3, $"stale hosted-node generations leaked: {alive} alive — {diag}");
			}
			finally
			{
				ReactiveScheduler.AfterFlush -= RunLayout;
			}
		}

		/// <summary>A hosted swap triggered OUTSIDE a flush (a tap handler) must stay a single
		/// generation: its env writes during Materialize may not run an inline flush whose
		/// AfterFlush layout re-enters the mid-build node (was: search pane double-built and
		/// its node state lost).</summary>
		[Fact]
		public void RefreshOutsideFlush_IsAtomic_SingleGeneration()
		{
			FakeHostedNode.Depth = 0;
			FakeHostedNode.MaxDepth = 0;
			FakeHostedNode.TotalRefreshes = 0;
			FakeHostedNode.Instances.Clear();

			var app = new MiniReply();
			var probe = new ProbeRoot(app);
			CometBackendBridge.Materialize(probe, Factory, Ctx);

			void RunLayout()
			{
				var layoutRoot = probe.BuiltView ?? probe;
				CometBackendLayoutEngine.Layout(layoutRoot, new Size(402, 874));
			}
			ReactiveScheduler.AfterFlush += RunLayout;
			try
			{
				RunLayout();
				var ldBefore = FakeHostedNode.Instances.FindAll(n => n is FakeListDetailNode).Count;

				// The tap-handler path: flip the signal from OUTSIDE any flush. The LD node's
				// Refresh materializes the detail pane, whose ListView rows write the
				// environment (.FontSize) — before the HoldFlushes fix this ran an inline
				// flush that re-entered the mid-build node and spawned a second generation.
				app.OpenedEmailId.Value = 2;
				app.DetailOpen.Value = true;

				var ld = FakeHostedNode.Instances.FindAll(n => n is FakeListDetailNode);
				Assert.Equal(ldBefore, ld.Count);   // no new node generations from one tap
				Assert.True(FakeHostedNode.MaxDepth <= 4,
					$"nested re-entry during an outside-flush Refresh: depth {FakeHostedNode.MaxDepth}");
			}
			finally
			{
				ReactiveScheduler.AfterFlush -= RunLayout;
			}
		}
	}
}
