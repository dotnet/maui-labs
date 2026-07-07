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

			// Gold ExtendedFAB (ReplyListContent.kt:113-126): tertiaryContainer, "Compose" +
			// edit icon, expanded at the top (!canScrollBackward); collapses once scrolled.
			// (The gold also re-expands on any upward scroll — lastScrolledBackward — that
			// direction signal is a fidelity-pass follow-up.)
			var extended = new Signal<bool>(true);
			list.ScrolledFromTop.PropertyChanged += (_, __) => extended.Value = !list.ScrolledFromTop.Peek();
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
					// TODO(fidelity): the real DockedSearchBar control replaces this pill
					// (backlog §3); collapsed-state look per gold ReplyAppBars.kt:84-125.
					SearchBarPill().Margin(left: 16, top: 16, right: 16, bottom: 16),
					list.FlexGrow(1).FlexBasis(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
				fab.HorizontalLayoutAlignment(LayoutAlignment.End)
					.VerticalLayoutAlignment(LayoutAlignment.End)
					.Margin(right: 16, bottom: 16),
			}.Background(T.Background);
		}

		static View SearchBarPill() => new HStack(spacing: 12)
		{
			new Icon("search").IconSize(24).Color(T.OnSurfaceVariant).Padding(new Thickness(16, 0, 0, 0)),
			new Text("Search emails").FontSize(16).Color(T.OnSurfaceVariant).FlexGrow(1),
			new Image("avatar_6").Frame(width: 32, height: 32).CornerRadius(16)
				.Margin(right: 12).FlexShrink(0),
		}
		.Frame(height: 56)
		.Background(T.SurfaceContainerHigh)
		.CornerRadius(28);

		// ── One list row (gold ReplyEmailListItem.kt:52-142). Outer wrapper carries the
		// h16/v4 gutter as PADDING (list rows are laid at full list width; root margins
		// aren't part of the row's own layout box); the inner card paints/clips. ──
		static View EmailListItem(ReplyEmail email) => new VStack(spacing: 0f)
		{
			new VStack(spacing: 0f)
			{
				new HStack(spacing: 0f)
				{
					new Image(email.Sender.Avatar).Frame(width: 40, height: 40).CornerRadius(20).FlexShrink(0),
					new VStack(spacing: 0f)
					{
						new Text(email.Sender.FirstName).FontSize(12).Color(T.OnSurface),
						new Text(email.CreatedAt).FontSize(12).Color(T.OnSurfaceVariant),
					}.Padding(new Thickness(12, 4, 12, 4)).FlexGrow(1).FlexBasis(0),
					new Icon("star_border").IconSize(24).Color(T.Outline)
						.Frame(width: 40, height: 40)
						.Background(T.SurfaceContainerHigh).CornerRadius(20)
						.FlexShrink(0),
				},
				new Text(email.Subject).FontSize(16).Color(T.OnSurface)
					.Padding(new Thickness(0, 12, 0, 8)),
				new Text(Preview(email.Body)).FontSize(14).Color(T.OnSurfaceVariant)
					.LineBreakMode(LineBreakMode.WordWrap),
			}
			.Padding(new Thickness(20))
			.Background(email.Id == OpenedEmailId.Value && DetailOpen.Value ? T.SecondaryContainer : T.SurfaceVariant)
			.CornerRadius(16)
			.OnTap(_ =>
			{
				OpenedEmailId.Value = (int)email.Id;
				DetailOpen.Value = true;
			}),
		}.Padding(new Thickness(16, 4, 16, 4));

		// 2-line preview stand-in until Text gains MaxLines/ellipsis (backlog).
		static string Preview(string body)
		{
			if (body.Length == 0)
				return "";
			var line = body.Split('\n')[0];
			return line.Length > 92 ? line[..92].TrimEnd() + " …" : line;
		}

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
		static View ThreadButton(string label) => new Text(label).FontSize(14).Color(T.OnSurface)
			.HorizontalLayoutAlignment(LayoutAlignment.Center)
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
