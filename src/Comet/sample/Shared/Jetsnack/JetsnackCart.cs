#nullable enable
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using T = CometSamples.Jetsnack.JetsnackTheme;

namespace CometSamples.Jetsnack
{
	/// <summary>
	/// My Cart (ui/home/cart/Cart.kt): "Order (N items)" header, cart rows (100dp circle
	/// image | name/tagline + price | X remove + Qty stepper), the Summary block
	/// (Subtotal / Shipping &amp; Handling / Total), "Inspired by your cart" collection, and
	/// the pinned gradient Checkout bar. Swipe-to-dismiss is a documented deviation
	/// (rows remove via the X, the gold's accessibility path).
	/// </summary>
	public static class JetsnackCart
	{
		sealed class Line
		{
			public Line(OrderLine source) { Snack = source.Snack; Count = source.Count; }
			public Snack Snack { get; }
			public int Count { get; set; }
		}

		static readonly List<Line> Lines = SnackRepo.GetCart().Select(l => new Line(l)).ToList();
		static ListView<object>? _list;

		const long ShippingCosts = 369;

		static IReadOnlyList<object> Rows()
		{
			var rows = new List<object> { "header" };
			rows.AddRange(Lines);
			rows.Add("summary");
			rows.Add("inspired");
			rows.Add("spacer");
			return rows;
		}

		public static View Screen(double topInset)
		{
			var list = new ListView<object>(Rows)
			{
				ViewFor = r => r switch
				{
					"header" => Header(),
					Line line => CartRow(line),
					"summary" => Summary(),
					"inspired" => JetsnackHome.RelatedCollection(SnackRepo.GetInspiredByCart()),
					_ => new HStack().Frame(height: 64),
				},
			};
			_list = list;

			return new VStack(spacing: 0f)
			{
				new HStack().Frame(height: (float)topInset).FlexShrink(0),
				JetsnackHome.DestinationBar().FlexShrink(0),
				new ZStack
				{
					list.HorizontalLayoutAlignment(LayoutAlignment.Fill)
						.VerticalLayoutAlignment(LayoutAlignment.Fill),
					new VStack(spacing: 0f)
					{
						new HStack().FlexGrow(1),
						CheckoutBar().FlexShrink(0),
					}
					.HorizontalLayoutAlignment(LayoutAlignment.Fill)
					.VerticalLayoutAlignment(LayoutAlignment.Fill),
				}.FlexGrow(1).FlexBasis(0),
			}
			.HorizontalLayoutAlignment(LayoutAlignment.Fill)
			.VerticalLayoutAlignment(LayoutAlignment.Fill)
			.Background(T.UiBackground);
		}

			static View Header() =>
			new Text(() => $"Order ({Lines.Sum(l => l.Count)} items)")
				.FontFamily("Montserrat").FontWeight(FontWeight.Semibold).FontSize(20).Color(T.Brand)
				.Padding(new Thickness(24, 16, 24, 4));

		// Cart.kt CartItem: image 100 (pad v16) | name titleMedium + tagline help + price |
		// X remove top-right; Qty stepper at the row's end.
		static View CartRow(Line line) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 0f)
			{
				new Image(line.Snack.ImageRes).Frame(width: 100, height: 100).CornerRadius(50)
					.Margin(new Thickness(0, 16, 0, 16))
					.VerticalLayoutAlignment(LayoutAlignment.Center).FlexShrink(0),
				new VStack(spacing: 0f)
				{
					T.TitleMedium(line.Snack.Name).Color(T.TextSecondary),
					// Gold CartItem: tagline (bodyLarge Karla, textHelp) sits between the
					// name and the price row, spacer 8 above the price.
					T.BodyLarge(line.Snack.Tagline.Length > 0 ? line.Snack.Tagline : "A tag line")
						.Color(T.TextHelp).Padding(new Thickness(0, 4, 0, 0)),
					new HStack().Frame(height: 8),
					new HStack(spacing: 0f)
					{
						T.TitleMedium(Jetsnack.FormatPrice(line.Snack.Price)).Color(T.TextPrimary)
							.VerticalLayoutAlignment(LayoutAlignment.Center),
						new HStack().FlexGrow(1),
						QuantityStepper(line),
					}.HorizontalLayoutAlignment(LayoutAlignment.Fill),
				}.Padding(new Thickness(16, 16, 0, 16)).FlexGrow(1).FlexBasis(0),
				new Icon("close").IconSize(18).Color(T.IconSecondary)
					.Frame(width: 40, height: 40).Padding(new Thickness(11))
					.FlexShrink(0)
					.OnTap(_ => { Lines.Remove(line); _list?.ReloadData(); }),
			}.Padding(new Thickness(24, 0, 12, 0)),
			JetsnackHome.Divider(1).Margin(new Thickness(24, 0, 24, 0)),
		};

		static View QuantityStepper(Line line) => new HStack(spacing: 4f)
		{
			new Text("Qty").FontFamily("Montserrat").FontSize(16).Color(T.TextSecondary)
				.Padding(new Thickness(0, 0, 14, 0))
				.VerticalLayoutAlignment(LayoutAlignment.Center),
			StepperCircle("remove", () => { if (line.Count > 1) { line.Count--; _list?.ReloadData(); } }),
			new Text(line.Count.ToString()).FontFamily("Karla").FontWeight(FontWeight.Bold).FontSize(18)
				.Color(T.TextPrimary)
				.Frame(width: 24)
				.HorizontalLayoutAlignment(LayoutAlignment.Center)
				.VerticalLayoutAlignment(LayoutAlignment.Center),
			StepperCircle("add", () => { line.Count++; _list?.ReloadData(); }),
		}.FlexShrink(0);

		/// <summary>JetsnackGradientTintedIconButton: a circle with the interactiveSecondary
		/// DIAGONAL-GRADIENT border over uiBackground (per-glyph gradient tint approximated
		/// with the ramp's Shadow3 — backlogged).</summary>
		internal static View StepperCircle(string icon, System.Action onTap) =>
			new Icon(icon).IconSize(16).Color(T.Shadow3)
				.Frame(width: 28, height: 28).Padding(new Thickness(6))
				.BorderGradient(T.Gradient2_2).CornerRadius(14)
				.Background(T.UiBackground)
				.VerticalLayoutAlignment(LayoutAlignment.Center)
				.OnTap(_ => onTap());

		// Cart.kt SummaryItem.
		static View Summary()
		{
			long subtotal = Lines.Sum(l => l.Snack.Price * l.Count);
			// Gold SummaryItem: label + amount both bodyLarge (Karla) in the DEFAULT dark
			// content color; only the Total's AMOUNT steps up to titleMedium.
			// The Total row's label hugs the amount (gold: weight(1f) +
			// wrapContentWidth(End) + padding end 16); the others sit at the start.
			View Row(string label, long amount, bool totalRow = false)
			{
				var amountText = (totalRow
					? T.TitleMedium(Jetsnack.FormatPrice(amount))
					: T.BodyLarge(Jetsnack.FormatPrice(amount))).Color(T.TextSecondary)
					.FlexShrink(0);
				var row = totalRow
					? new HStack(spacing: 0f)
					{
						new HStack().FlexGrow(1),
						T.BodyLarge(label).Color(T.TextSecondary)
							.Padding(new Thickness(0, 0, 16, 0)).FlexShrink(0),
						amountText,
					}
					: new HStack(spacing: 0f)
					{
						T.BodyLarge(label).Color(T.TextSecondary)
							.FlexGrow(1).FlexBasis(0),
						amountText,
					};
				return row.Padding(new Thickness(24, 6, 24, 6)).HorizontalLayoutAlignment(LayoutAlignment.Fill);
			}

			return new VStack(spacing: 0f)
			{
				T.TitleLarge("Summary").Color(T.Brand)
					.Padding(new Thickness(24, 16, 24, 8)),
				Row("Subtotal", subtotal),
				Row("Shipping & Handling", ShippingCosts),
				new HStack().Frame(height: 8),
				JetsnackHome.Divider(1).Margin(new Thickness(24, 0, 24, 0)),
				new HStack().Frame(height: 8),
				Row("Total", subtotal + ShippingCosts, totalRow: true),
				new HStack().Frame(height: 8),
				JetsnackHome.Divider(1),
			};
		}

		// Cart.kt CheckoutBar: divider + gradient full-width Checkout button.
		static View CheckoutBar() => new VStack(spacing: 0f)
		{
			JetsnackHome.Divider(1),
			new VStack(spacing: 0f)
			{
				new HStack(spacing: 0f)
				{
					new HStack().FlexGrow(1),
					T.LabelLarge("Checkout")
						.Color(T.TextInteractive).MaxLines(1)
						.VerticalLayoutAlignment(LayoutAlignment.Center),
					new HStack().FlexGrow(1),
				}
				.Frame(height: 36)
				.BackgroundGradient(T.Tornado1).CornerRadius(18)
				.Margin(new Thickness(24, 10, 24, 10))
				.HorizontalLayoutAlignment(LayoutAlignment.Fill),
			},
		}.Background(T.UiBackground.WithAlpha(0.95f));
	}
}
