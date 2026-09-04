#nullable enable
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Reply.ReplyTheme;

namespace CometSamples.Reply
{
	/// <summary>
	/// The Reply screens, values-from-source (file:line cites into the gold Kotlin app).
	/// Inbox = ReplyListContent.kt; rows = ReplyEmailListItem.kt; detail = ReplyEmailDetail +
	/// EmailDetailAppBar + ReplyEmailThreadItem; coming-soon = EmptyComingSoon.kt.
	/// </summary>
	public static class ReplyScreens
	{
		public static readonly Signal<bool> DetailOpen = new(false);
		public static readonly Signal<int> OpenedEmailId = new(0);

		// Hook the static signals ONCE (a per-build += on a static signal stacks a handler —
		// and roots the captured list — on every body rebuild); the fields track the current
		// list instances.
		static ListView<ReplyEmail>? _inboxList, _searchResults;
		static ReplyScreens()
		{
			SearchQuery.PropertyChanged += (_, __) => _searchResults?.ReloadData();
			// Rows snapshot OpenedEmailId at build; reload so the opened highlight moves.
			OpenedEmailId.PropertyChanged += (_, __) => _inboxList?.ReloadData();
		}

		// ── Selection mode (gold ReplyHomeViewModel.toggleSelectedEmail + ReplyEmailListItem
		// combinedClickable onLongClick): long-press a row (or tap its avatar) to toggle it
		// into the selection; selected rows show primaryContainer + a check avatar. ──
		static readonly HashSet<long> SelectedIds = new();
		static void ToggleSelection(long id)
		{
			if (!SelectedIds.Add(id))
				SelectedIds.Remove(id);
			_inboxList?.ReloadData();
		}

		static ReplyEmail Opened()
		{
			foreach (var e in ReplyData.AllEmails)
				if (e.Id == OpenedEmailId.Value)
					return e;
			return ReplyData.AllEmails[0];
		}

		// ── Inbox route: the ListDetail scaffold (gold ReplyInboxScreen) ──
		public static View Inbox() => new ListDetail(DetailOpen, InboxList(), EmailDetail());

		// ── The email list (gold ReplyEmailList :162-205): search bar pinned on top,
		// list under it, ExtendedFAB bottom-end (compact only in gold; simplified: always). ──
		static View InboxList()
		{
			var list = new ListView<ReplyEmail>(() => ReplyData.AllEmails)
			{
				ViewFor = email => EmailListItem(email),
			};
			_inboxList = list;

			// Gold ExtendedFAB (ReplyListContent.kt:113-126): tertiaryContainer, "Compose" +
			// edit icon, expanded at the top (!canScrollBackward); collapses once scrolled.
			// (The gold also re-expands on any upward scroll — lastScrolledBackward — that
			// direction signal is a fidelity-pass follow-up.)
			var extended = new Signal<bool>(true);
			// Gold driver (ReplyListContent.kt:124-125): expanded = lastScrolledBackward ||
			// !canScrollBackward — re-expands on ANY upward scroll, not only at the very top.
			void UpdateFab() => extended.Value =
				list.LastScrolledBackward.Peek() || !list.ScrolledFromTop.Peek();
			list.ScrolledFromTop.PropertyChanged += (_, __) => UpdateFab();
			list.LastScrolledBackward.PropertyChanged += (_, __) => UpdateFab();
			var fab = new Fab(
					icon: new Icon("edit").IconSize(18).Color(T.OnTertiaryContainer),
					label: new Text("Compose").FontSize(14).Color(T.OnTertiaryContainer),
					onClick: () => { },
					height: 56,
					containerColor: T.TertiaryContainer,
					contentColor: T.OnTertiaryContainer)
				.ExtendedWhen(extended);

			return new ZStack
			{
				new VStack(spacing: 0f)
				{
					// The REAL M3 docked search bar (gold ReplyDockedSearchBar,
					// ReplyAppBars.kt:84-168): "Search emails" placeholder + profile
					// trailing; expanded popup shows history/results.
					EmailSearchBar().Margin(left: 16, top: 16, right: 16, bottom: 16),
					list.FlexGrow(1).FlexBasis(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
				fab.HorizontalLayoutAlignment(LayoutAlignment.End)
					.VerticalLayoutAlignment(LayoutAlignment.End)
					.Margin(right: 16, bottom: 16),
			}.Background(T.Background);
		}

		static readonly Signal<string> SearchQuery = new("");

		// Gold filter (ReplyAppBars.kt:65-82): subject OR sender fullName startsWith,
		// ignore case; empty query → history message.
		static IReadOnlyList<ReplyEmail> SearchResults()
		{
			var q = SearchQuery.Peek();
			if (string.IsNullOrEmpty(q))
				return System.Array.Empty<ReplyEmail>();
			var hits = new List<ReplyEmail>();
			foreach (var e in ReplyData.AllEmails)
				if (e.Subject.StartsWith(q, System.StringComparison.OrdinalIgnoreCase)
					|| e.Sender.FullName.StartsWith(q, System.StringComparison.OrdinalIgnoreCase))
					hits.Add(e);
			return hits;
		}

		static View EmailSearchBar()
		{
			var results = new ListView<ReplyEmail>(() => SearchResults())
			{
				ViewFor = e => SearchResultRow(e),
			};
			_searchResults = results;

			var content = new VStack(spacing: 0f)
			{
				// Gold: "No search history" (empty) / "No item found" (no hits) / results.
				new Text(() => SearchQuery.Value.Length == 0 ? "No search history"
						: SearchResults().Count == 0 ? "No item found" : "")
					.FontSize(16).Color(T.OnSurface)
					.Padding(new Thickness(16)),
				results.FlexGrow(1).FlexBasis(0),
			}.Frame(height: 420);

			return new SearchBar(SearchQuery,
				placeholder: new Text("Search emails").FontSize(16).Color(T.OnSurfaceVariant),
				content: content,
				leading: new Icon("search").IconSize(24).Color(T.OnSurfaceVariant),
				trailing: new Image("avatar_6").Frame(width: 32, height: 32).CornerRadius(16),
				containerColor: T.SurfaceContainerHigh);
		}

		// Gold search result rows: M3 ListItem shape — 32dp avatar, subject headline,
		// sender supporting (ReplyAppBars.kt:137-155).
		static View SearchResultRow(ReplyEmail email) => new HStack(spacing: 16f)
		{
			new Image(email.Sender.Avatar).Frame(width: 32, height: 32).CornerRadius(16).FlexShrink(0),
			new VStack(spacing: 2f)
			{
				new Text(email.Subject).FontSize(16).Color(T.OnSurface),
				new Text(email.Sender.FullName).FontSize(14).Color(T.OnSurfaceVariant),
			}.FlexGrow(1).FlexBasis(0),
		}
		.Padding(new Thickness(16, 8, 16, 8))
		.OnTap(_ =>
		{
			OpenedEmailId.Value = (int)email.Id;
			DetailOpen.Value = true;
		});

		// ── One list row (gold ReplyEmailListItem.kt:52-142). Outer wrapper carries the
		// h16/v4 gutter as PADDING (list rows are laid at full list width; root margins
		// aren't part of the row's own layout box); the inner card paints/clips. ──
		// Selected avatar (gold SelectedProfileImage): primary circle + centered onPrimary
		// check; tapping either avatar state toggles selection (gold clickModifier).
		static View AvatarSlot(ReplyEmail email) => (SelectedIds.Contains(email.Id)
			? (View)new ZStack { new Icon("check").IconSize(24).Color(T.OnPrimary) }
				.Background(T.Primary)
			: new Image(email.Sender.Avatar))
			.Frame(width: 40, height: 40).CornerRadius(20).FlexShrink(0)
			.OnTap(_ => ToggleSelection(email.Id));

		static View EmailListItem(ReplyEmail email) => new VStack(spacing: 0f)
		{
			new VStack(spacing: 0f)
			{
				new HStack(spacing: 0f)
				{
					AvatarSlot(email),
					new VStack(spacing: 0f)
					{
						new Text(email.Sender.FirstName).FontSize(12).FontWeight(FontWeight.Medium).Color(T.OnSurface),
						new Text(email.CreatedAt).FontSize(12).FontWeight(FontWeight.Medium).Color(T.OnSurfaceVariant),
					}.Padding(new Thickness(12, 4, 12, 4)).FlexGrow(1).FlexBasis(0),
					new Icon("star_border").IconSize(24).Color(T.Outline)
						.Frame(width: 40, height: 40)
						.Background(T.SurfaceContainerHigh).CornerRadius(20)
						.FlexShrink(0),
				},
				new Text(email.Subject).FontSize(16).Color(T.OnSurface)
					.Padding(new Thickness(0, 12, 0, 8)),
				// Gold: bodyMedium, maxLines 2, ellipsis, RAW line breaks kept
				// (ReplyEmailListItem.kt:134-139 — short first lines ellipsize on line 2).
				new Text(email.Body).FontSize(14).Color(T.OnSurfaceVariant)
					.LineBreakMode(LineBreakMode.WordWrap)
					.MaxLines(2),
			}
			.Padding(new Thickness(20))
			// Gold precedence (:72-76): SELECTED wins (primaryContainer), then OPENED
			// (secondaryContainer — the home state opens the first email at launch), else
			// surfaceVariant.
			.Background(SelectedIds.Contains(email.Id) ? T.PrimaryContainer
				: email.Id == OpenedEmailId.Value ? T.SecondaryContainer : T.SurfaceVariant)
			.CornerRadius(16)
			.OnTap(_ =>
			{
				OpenedEmailId.Value = (int)email.Id;
				DetailOpen.Value = true;
			})
			// Gold combinedClickable onLongClick: long-press toggles selection.
			.OnLongPress(_ => ToggleSelection(email.Id)),
		}.Padding(new Thickness(16, 4, 16, 4));

		// ── Detail pane (gold ReplyEmailDetail :207-225 + EmailDetailAppBar) ──
		static View EmailDetail()
		{
			var stack = new VStack(spacing: 0f) { DetailAppBar() };
			var threads = new ListView<ReplyEmail>(() => Opened().Threads ?? ReplyData.Threads)
			{
				ViewFor = t => ThreadItem(t),
			};
			stack.Add(threads.FlexGrow(1).FlexBasis(0));
			return stack.Background(T.InverseOnSurface);
		}

		// Gold EmailDetailAppBar (ReplyAppBars.kt:173-228): inverseOnSurface bar; back
		// FilledIconButton; centered subject titleMedium + "N Messages" labelMedium.
		static View DetailAppBar() => new HStack(spacing: 0f)
		{
			new Icon("arrow_back").IconSize(14).Color(T.OnSurface)
				.Padding(new Thickness(13))   // centers the 14dp glyph in the 40dp circle
				.Frame(width: 40, height: 40)
				.Background(T.Surface).CornerRadius(20)
				.Margin(left: 8, top: 8, bottom: 8)
				.FlexShrink(0)
				.OnTap(_ => DetailOpen.Value = false),
			new VStack(spacing: 2f)
			{
				new Text(() => Opened().Subject).FontSize(16).Color(T.OnSurfaceVariant)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
				new Text(() => $"{(Opened().Threads ?? ReplyData.Threads).Count} Messages").FontSize(12).Color(T.Outline)
					.HorizontalLayoutAlignment(LayoutAlignment.Center),
			}.FlexGrow(1).FlexBasis(0).VerticalLayoutAlignment(LayoutAlignment.Center),
			new Icon("more_vert").IconSize(24).Color(T.OnSurfaceVariant)
				.Frame(width: 48, height: 48)
				.Margin(right: 4)
				.FlexShrink(0),
		}.Frame(height: 64);

		// ── One thread item (gold ReplyEmailThreadItem.kt:44-136); same wrapper/card split
		// as the list rows. ──
		static View ThreadItem(ReplyEmail email) => new VStack(spacing: 0f)
		{
			new VStack(spacing: 0f)
			{
				new HStack(spacing: 0f)
				{
					new Image(email.Sender.Avatar).Frame(width: 40, height: 40).CornerRadius(20).FlexShrink(0),
					new VStack(spacing: 0f)
					{
						new Text(email.Sender.FirstName).FontSize(12).Color(T.OnSurface),
						new Text("20 mins ago").FontSize(12).Color(T.Outline),   // gold hardcodes it (:72)
					}.Padding(new Thickness(12, 4, 12, 4)).FlexGrow(1).FlexBasis(0),
					new Icon("star_border").IconSize(24).Color(T.Outline)
						.Frame(width: 40, height: 40)
						.Background(T.SurfaceContainer).CornerRadius(20)
						.FlexShrink(0),
				},
				new Text(email.Subject).FontSize(14).Color(T.Outline)
					.Padding(new Thickness(0, 12, 0, 8)),
				new Text(email.Body).FontSize(16).Color(T.OnSurfaceVariant)
					.LineBreakMode(LineBreakMode.WordWrap),
				new HStack(spacing: 12f)
				{
					ThreadButton("Reply"),
					ThreadButton("Reply All"),
				}.Padding(new Thickness(0, 20, 0, 8)),
			}
			.Padding(new Thickness(20))
			.Background(T.SurfaceContainerHigh)
			.CornerRadius(16),
		}.Padding(new Thickness(16, 4, 16, 4));

		// Gold: M3 Button, containerColor surfaceBright, onSurface text, weight(1f) each.
		// Spacer rows/columns center the label (alignment positions a view in its parent).
		static View ThreadButton(string label) => new HStack(spacing: 0f)
		{
			new HStack().FlexGrow(1),
			new VStack(spacing: 0f)
			{
				new HStack().FlexGrow(1),
				new Text(label).FontSize(14).Color(T.OnSurface),
				new HStack().FlexGrow(1),
			},
			new HStack().FlexGrow(1),
		}
		.Frame(height: 40)
		.Background(T.SurfaceBright)
		.CornerRadius(20)
		.FlexGrow(1).FlexBasis(0);

		// ── EmptyComingSoon.kt ──
		public static View ComingSoon() => new VStack(spacing: 8f)
		{
			new HStack().FlexGrow(1),
			new Text("Screen under construction").FontSize(22).FontWeight(FontWeight.Bold)
				.Color(T.Primary).HorizontalLayoutAlignment(LayoutAlignment.Center),
			new Text("This screen is still under construction. This sample will help you learn\nabout adaptive layouts in Jetpack Compose")
				.FontSize(12).Color(T.Outline)
				.LineBreakMode(LineBreakMode.WordWrap)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.Padding(new Thickness(24, 0)),
			new HStack().FlexGrow(1),
		}.Background(T.Background);
	}
}
