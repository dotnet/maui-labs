#nullable enable
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.JetNews.JetNewsTheme;

namespace CometSamples.JetNews
{
	/// <summary>
	/// The Interests screen (InterestsScreen.kt): center top bar, REAL PrimaryTabRow
	/// (Topics / People / Publications), and per-tab selectable topic lists.
	/// Gold renders the lists as Column+verticalScroll; here they're ListViews (LazyColumn)
	/// so toggles re-pull rows — documented deviation, visually identical on compact.
	/// </summary>
	public static class JetNewsInterests
	{
		// FakeInterestsRepository verbatim.
		static readonly IReadOnlyList<(string Section, IReadOnlyList<string> Topics)> Topics = new (string, IReadOnlyList<string>)[]
		{
			("Android", new[] { "Jetpack Compose", "Kotlin", "Jetpack" }),
			("Programming", new[] { "Kotlin", "Declarative UIs", "Java", "Unidirectional Data Flow", "C++" }),
			("Technology", new[] { "Pixel", "Google" }),
		};
		static readonly IReadOnlyList<string> People = new[]
		{
			"Kobalt Toral", "K'Kola Uvarek", "Kris Vriloc", "Grala Valdyr", "Kruel Valaxar",
			"L'Elij Venonn", "Kraag Solazarn", "Tava Targesh", "Kemarrin Muuda",
		};
		static readonly IReadOnlyList<string> Publications = new[]
		{
			"Kotlin Vibe", "Compose Mix", "Compose Breakdown", "Android Pursue", "Kotlin Watchman",
			"Jetpack Ark", "Composeshack", "Jetpack Point", "Compose Tribune",
		};

		static readonly Signal<int> TabIndex = new(0);
		static readonly HashSet<string> Selected = new();   // keyed "{section}|{topic}" / name
		static readonly List<ListView<Row>> Lists = new();

		static void Toggle(string key)
		{
			if (!Selected.Add(key))
				Selected.Remove(key);
			foreach (var list in Lists)
				list.ReloadData();
		}

		abstract record Row;
		sealed record SectionRow(string Title) : Row;
		sealed record TopicRow(string Key, string Title) : Row;

		static Text Tx(string s) => new Text(s).FontFamily("Montserrat");

		public static View Screen(double topInset, System.Action openDrawer)
		{
			JetNewsIcons.Register();
			Lists.Clear();

			var tabs = new TabBar(TabIndex, new[] { "Topics", "People", "Publications" },
				selectedColor: T.Primary, unselectedColor: T.OnSurface.WithAlpha(0.8f),
				fontFamily: "Montserrat");

			var content = new ContentSwitcher(TabIndex, new View[]
			{
				TabList(TopicsRows()),
				TabList(NamesRows(People)),
				TabList(NamesRows(Publications)),
			});

			return new VStack(spacing: 0f)
			{
				new HStack().Frame(height: (float)topInset).FlexShrink(0),
				TopBar(openDrawer).FlexShrink(0),
				tabs.FlexShrink(0),
				new HStack().Frame(height: 1).Background(T.OnSurface.WithAlpha(0.1f))
					.HorizontalLayoutAlignment(LayoutAlignment.Fill).FlexShrink(0),
				content.FlexGrow(1).FlexBasis(0),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.Background);
		}

		// CenterAlignedTopAppBar: nav = brand logo (menu stand-in, like Home), centered
		// "Interests" titleLarge tinted primary, search action.
		static View TopBar(System.Action openDrawer) => new HStack(spacing: 0f)
		{
			new Icon("jetnews_logo").IconSize(24).Color(T.Primary)
				.Frame(width: 48, height: 48).Padding(new Thickness(12))
				.FlexShrink(0)
				.OnTap(_ => openDrawer()),
			new HStack().FlexGrow(1),
			Tx("Interests").FontSize(22).Color(T.Primary)
				.VerticalLayoutAlignment(LayoutAlignment.Center),
			new HStack().FlexGrow(1),
			new Icon("search").IconSize(24).Color(T.OnSurfaceVariant)
				.Frame(width: 48, height: 48).Padding(new Thickness(12))
				.FlexShrink(0),
		}.Frame(height: 64);

		static IReadOnlyList<Row> TopicsRows()
		{
			var rows = new List<Row>();
			foreach (var (section, topics) in Topics)
			{
				rows.Add(new SectionRow(section));
				foreach (var topic in topics)
					rows.Add(new TopicRow($"{section}|{topic}", topic));
			}
			return rows;
		}

		static IReadOnlyList<Row> NamesRows(IReadOnlyList<string> names)
		{
			var rows = new List<Row> { new SectionRow("") };   // gold pads the list top 16
			foreach (var name in names)
				rows.Add(new TopicRow(name, name));
			return rows;
		}

		static View TabList(IReadOnlyList<Row> rows)
		{
			var list = new ListView<Row>(() => rows)
			{
				ViewFor = r => r switch
				{
					SectionRow s when s.Title.Length > 0 =>
						Tx(s.Title).FontSize(16).FontWeight(FontWeight.Medium).Color(T.OnSurface)
							.Padding(new Thickness(16)),
					SectionRow => new HStack().Frame(height: 16),
					TopicRow t => TopicItem(t),
					_ => new HStack(),
				},
			};
			Lists.Add(list);
			return list;
		}

		// TopicItem: placeholder image 56 r4 | title titleMedium pad 16 | select toggle 36;
		// divider inset start 72, v-pad 8 (baked into the row column).
		static View TopicItem(TopicRow t) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				new Image("placeholder_1_1").Frame(width: 56, height: 56).CornerRadius(4)
					.FlexShrink(0),
				Tx(t.Title).FontSize(16).FontWeight(FontWeight.Medium).Color(T.OnSurface)
					.LineBreakMode(LineBreakMode.WordWrap).LineBreak(TextLineBreak.Heading)
					.Padding(new Thickness(16)).FlexGrow(1).FlexBasis(0)
					.VerticalLayoutAlignment(LayoutAlignment.Center),
				new HStack().Frame(width: 16),
				SelectTopicButton(Selected.Contains(t.Key))
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
			},
			new HStack().Frame(height: 1).Background(T.OnSurface.WithAlpha(0.1f))
				.Margin(new Thickness(56, 8, 0, 8))
				.HorizontalLayoutAlignment(LayoutAlignment.Fill),
		}
		.Padding(new Thickness(16, 0, 16, 0))
		.OnTap(_ => Toggle(t.Key));

		// SelectTopicButton.kt: 36dp circle Surface — unselected: onPrimary bg, onSurface@10%
		// border, primary "+"; selected: primary bg, primary border, onPrimary "✓".
		static View SelectTopicButton(bool selected) =>
			new Icon(selected ? "check" : "add").IconSize(20)
				.Color(selected ? T.OnPrimary : T.Primary)
				.Frame(width: 36, height: 36)
				.Background(selected ? T.Primary : T.OnPrimary)
				.CornerRadius(18)
				.Border(1, selected ? T.Primary : T.OnSurface.WithAlpha(0.1f));
	}
}
