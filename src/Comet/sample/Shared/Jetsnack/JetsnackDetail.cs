#nullable enable
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetsnack.JetsnackTheme;

namespace CometSamples.Jetsnack
{
	/// <summary>
	/// Snack detail (ui/snackdetail/SnackDetail.kt), values-from-source: gradient header
	/// (280dp), 300dp circular image overlapping into the title block, italic
	/// name/tagline + brand price, Details + Ingredients sections, related collections,
	/// and the pinned cart bar (Qty stepper + gradient ADD TO CART). STATIC layout —
	/// the gold's scroll parallax (Title/Image offset math) is a documented deviation.
	/// </summary>
	public static class JetsnackDetail
	{
		const float BottomBarHeight = 56f;

		static readonly Signal<int> Count = new(1);
		static readonly Signal<int> BodyVersion = new(0);

		static ListView<object>? _list;

		public static View Screen(Signal<long> snackId, double topInset, System.Action onBack)
		{
			JetsnackIcons.Register();

			var list = new ListView<object>(() => new object[] { snackId.Peek() })
			{
				ViewFor = _ => Body(SnackRepo.GetSnack(snackId.Peek()), topInset),
			};
			_list = list;
			if (!_hooked)
			{
				_hooked = true;
				snackId.PropertyChanged += (_, _) => { Count.Value = 1; _list?.ReloadData(); };
				Count.PropertyChanged += (_, _) => _list?.ReloadData();
			}

			return new ZStack
			{
				new VStack(spacing: 0f)
				{
					list.FlexGrow(1).FlexBasis(0),
					CartBar().FlexShrink(0),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
				// Up button floats over the header (SnackDetail.kt Up: 36dp circle,
				// pad 16/10 below the status bar).
				new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)(topInset + 10)).FlexShrink(0),
					new HStack(spacing: 0f)
					{
						new Icon("arrow_back").IconSize(20).Color(T.IconInteractive)
							.Frame(width: 36, height: 36).Padding(new Thickness(8))
							.Background(T.Neutral8.WithAlpha(0.32f)).CornerRadius(18)
							.Margin(new Thickness(16, 0, 0, 0))
							.OnTap(_ => onBack()),
						new HStack().FlexGrow(1),
					}.FlexShrink(0),
					new HStack().FlexGrow(1),
				}
				.HorizontalLayoutAlignment(LayoutAlignment.Fill)
				.VerticalLayoutAlignment(LayoutAlignment.Fill),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.UiBackground);
		}

		static bool _hooked;

		static string FormatPrice(long price) => $"${price / 100}.{price % 100:00}";

		// The scrolling body — one row holding the whole column (Column+verticalScroll in
		// the gold; a single-row LazyColumn here so ReloadData re-binds on snack change).
		static View Body(Snack snack, double topInset) => new VStack(spacing: 0f)
		{
			// Header: 280dp gradient band with the 300dp circular image overlapping below.
			new ZStack
			{
				new VStack(spacing: 0f) { new HStack().Frame(height: 280) }
					.Frame(height: 280)
					.BackgroundGradient(T.Tornado1)
					.HorizontalLayoutAlignment(LayoutAlignment.Fill)
					.VerticalLayoutAlignment(LayoutAlignment.Start),
				new VStack(spacing: 0f)
				{
					new HStack().Frame(height: (float)(topInset + 56)),
					new Image(snack.ImageRes).Frame(width: 300, height: 300).CornerRadius(150)
						.HorizontalLayoutAlignment(LayoutAlignment.Center),
				}.HorizontalLayoutAlignment(LayoutAlignment.Center),
			}
			.Frame(height: (float)(topInset + 380))
			.HorizontalLayoutAlignment(LayoutAlignment.Fill),

			// Title block (Title): name headlineMedium ITALIC textSecondary, tagline
			// titleSmall 20 italic textHelp, price brand, divider.
			new VStack(spacing: 0f)
			{
				new HStack().Frame(height: 16),
				new Text(snack.Name).FontSize(28).FontSlant(Microsoft.Maui.FontSlant.Italic).Color(T.TextSecondary)
					.Padding(new Thickness(24, 0, 24, 0)),
				new Text(snack.Tagline).FontSize(20).FontSlant(Microsoft.Maui.FontSlant.Italic).Color(T.TextHelp)
					.Padding(new Thickness(24, 0, 24, 0)),
				new HStack().Frame(height: 4),
				new Text(FormatPrice(snack.Price)).FontSize(16).FontWeight(FontWeight.Bold).Color(T.Brand)
					.Padding(new Thickness(24, 0, 24, 8)),
				JetsnackHome.Divider(1),
			},

			// Body: Details overline + lorem placeholder + SEE MORE, Ingredients, divider,
			// related collections (Customers also bought / Popular).
			new VStack(spacing: 0f)
			{
				new HStack().Frame(height: 16),
				new Text("Details").FontSize(11).FontWeight(FontWeight.Medium).Color(T.TextHelp)
					.Padding(new Thickness(24, 0, 24, 0)),
				new HStack().Frame(height: 16),
				new Text(DetailPlaceholder).FontSize(16).LineHeight(24).Color(T.TextHelp)
					.LineBreakMode(LineBreakMode.WordWrap).MaxLines(_seeMore ? 0 : 6)
					.Padding(new Thickness(24, 0, 24, 0)),
				new Text(_seeMore ? "SEE LESS" : "SEE MORE").FontSize(14).FontWeight(FontWeight.Medium)
					.Color(T.TextLink)
					.Padding(new Thickness(24, 15, 24, 0))
					.HorizontalLayoutAlignment(LayoutAlignment.Center)
					.OnTap(_ => { _seeMore = !_seeMore; BodyVersion.Value++; _list?.ReloadData(); }),
				new HStack().Frame(height: 40),
				new Text("Ingredients").FontSize(11).FontWeight(FontWeight.Medium).Color(T.TextHelp)
					.Padding(new Thickness(24, 0, 24, 0)),
				new HStack().Frame(height: 4),
				new Text("Vanilla, Almond Flour, Eggs, Butter, Cream, Sugar")
					.FontSize(16).Color(T.TextHelp)
					.LineBreakMode(LineBreakMode.WordWrap)
					.Padding(new Thickness(24, 0, 24, 0)),
				new HStack().Frame(height: 16),
				JetsnackHome.Divider(1),
			},

			// Related collections (SnackRepo.getRelated) — the Home circle-row sections.
			RelatedSection(snack.Id),
			new HStack().Frame(height: BottomBarHeight + 8),
		};

		static bool _seeMore;

		static View RelatedSection(long snackId)
		{
			var column = new VStack(spacing: 0f);
			foreach (var collection in SnackRepo.GetRelated(snackId))
				column.Add(JetsnackHome.RelatedCollection(collection));
			return column;
		}

		/// <summary>CartBottomBar: divider + [QuantitySelector | gradient ADD TO CART].</summary>
		static View CartBar()
		{
			var bar = new VStack(spacing: 0f)
			{
				JetsnackHome.Divider(1),
				new HStack(spacing: 0f)
				{
					QuantitySelector(),
					new HStack().Frame(width: 16),
					new HStack(spacing: 0f)
					{
						new HStack().FlexGrow(1),
						new Text("ADD TO CART").FontSize(14).FontWeight(FontWeight.Bold)
							.Color(T.TextInteractive).MaxLines(1)
							.VerticalLayoutAlignment(LayoutAlignment.Center),
						new HStack().FlexGrow(1),
					}
					.Frame(height: 36)
					.BackgroundGradient(T.Tornado1).CornerRadius(18)
					.VerticalLayoutAlignment(LayoutAlignment.Center)
					.FlexGrow(1).FlexBasis(0),
				}
				.Frame(height: BottomBarHeight)
				.Padding(new Thickness(24, 0, 24, 0)),
				new HStack().Frame(height: 24),   // gesture-nav inset
			}.Background(T.UiBackground);
			return bar;
		}

		// QuantitySelector.kt: "Qty" label (end pad 18) + gradient-tinted −/+ around the count.
		static View QuantitySelector() => new HStack(spacing: 0f)
		{
			new Text("Qty").FontSize(11).FontWeight(FontWeight.Medium).Color(T.TextHelp)
				.Padding(new Thickness(0, 0, 18, 0))
				.VerticalLayoutAlignment(LayoutAlignment.Center),
			new Icon("remove").IconSize(20).Color(T.Brand)
				.Frame(width: 36, height: 36).Padding(new Thickness(8))
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.OnTap(_ => { if (Count.Peek() > 0) Count.Value = Count.Peek() - 1; }),
			new Text(() => Count.Value.ToString()).FontSize(18).Color(T.TextPrimary)
				.Frame(width: 24)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.VerticalLayoutAlignment(LayoutAlignment.Center),
			new Icon("add").IconSize(20).Color(T.Brand)
				.Frame(width: 36, height: 36).Padding(new Thickness(8))
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.OnTap(_ => Count.Value = Count.Peek() + 1),
		}.FlexShrink(0);

		const string DetailPlaceholder =
			"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Ut tempus, sem vitae " +
			"convallis imperdiet, lectus nunc pharetra diam, ac rhoncus quam eros eu risus. " +
			"Nulla pulvinar condimentum erat, pulvinar tempus turpis blandit ut. Etiam sed " +
			"ipsum sed lacus eleifend hendrerit eu quis quam. Etiam ligula eros, finibus " +
			"vestibulum tortor ac, ultrices accumsan dolor. Vivamus vel nisl a libero " +
			"lobortis posuere. Aenean facilisis nibh vel ultrices bibendum. Pellentesque " +
			"habitant morbi tristique senectus et netus et malesuada fames ac turpis " +
			"egestas. Suspendisse ac est vitae lacus commodo efficitur at ut massa. Etiam " +
			"vestibulum sit amet sapien sed varius. Aliquam non ipsum imperdiet, pulvinar " +
			"enim nec, mollis risus. Fusce id tincidunt nisl.";
	}
}
