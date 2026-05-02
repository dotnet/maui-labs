using Microsoft.Maui.Controls;
using GradientStop = Microsoft.Maui.Controls.GradientStop;
using LinearGradientBrush = Microsoft.Maui.Controls.LinearGradientBrush;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometMarvelousApp.Models;

record Illustration(
	WonderType Type,
	string Title,
	string MainObject,
	string Photo1,
	string Photo2,
	string Photo3,
	string Photo4,
	string Map,
	string WonderButton,
	string Flattened,
	double ScaleX,
	double ScaleY,
	double MarginTop,
	double MarginLeft,
	Color PrimaryColor,
	Color SecondaryColor,
	IllustrationImage[] BackgroundImages,
	IllustrationImage MainObjectImage,
	IllustrationImage MainObjectEditorialImage,
	IllustrationImage[] ForegroundImages
	)
{
	Brush? _backgroundBrush;
	public Brush BackgroundBrush
		=> _backgroundBrush ??= new LinearGradientBrush(new GradientStopCollection
		{
			new GradientStop(SecondaryColor, 0),
			new GradientStop(SecondaryColor, 0.5f),
			new GradientStop(PrimaryColor, 1),
		}, new Point(0, 0), new Point(0, 1));

	Brush? _foregroundBrush;
	public Brush ForegroundBrush
		=> _foregroundBrush ??= new LinearGradientBrush(new GradientStopCollection
		{
			new GradientStop(SecondaryColor.WithAlpha(0.0f), 0),
			new GradientStop(SecondaryColor.WithAlpha(0.0f), 0.5f),
			new GradientStop(PrimaryColor, 1),
		}, new Point(0, 0), new Point(0, 1));

	Brush? _secondaryBrush;
	public Brush SecondaryBrush
		=> _secondaryBrush ??= new SolidColorBrush(SecondaryColor);

	public static IReadOnlyDictionary<WonderType, Illustration> Config { get; } = new Dictionary<WonderType, Illustration>()
	{
		{
			WonderType.Colosseum, new Illustration(
				Type: WonderType.Colosseum,
				Title: "Colosseum",
				MainObject: "colosseum_colosseum.png",
				Photo1: "colosseum_photo_1.jpg",
				Photo2: "colosseum_photo_2.jpg",
				Photo3: "colosseum_photo_3.jpg",
				Photo4: "colosseum_photo_4.jpg",
				Map: "colosseum_map.jpg",
				WonderButton: "colosseum_wonder_button.png",
				Flattened: "colosseum_flattened.jpg",
				ScaleX: 1.76,
				ScaleY: 1.76,
				MarginTop: 0,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#1c746b"),
				SecondaryColor: Color.FromArgb("#47a49b"),
				BackgroundImages:
				[
					new IllustrationImage("colosseum_sun.png", new Rect(0.1, 0.1, 0.54, 0.54), new Rect(0.1, -0.02, 0.54, 0.54)),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, 0.13, 0.5, 0.5), new Rect(0.1, 0.13, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(1.2, 0.14, 0.34, 0.34), new Rect(-0.06, 0.14, 0.34, 0.34), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.05, 0.6, 0.6), new Rect(0.2, -0.05, 0.6, 0.6), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("colosseum_colosseum.png", new Rect(-0.38, -0.38, 1.76, 1.76), new Rect(-0.38, -0.38, 1.76, 1.76)),
				MainObjectEditorialImage: new IllustrationImage("colosseum_colosseum.png", new Rect(0.05, 0.2, 0.9, 0.9), new Rect(0.05, 0.2, 0.9, 0.9)),
				ForegroundImages:
				[
					new IllustrationImage("colosseum_foreground_left.png", new Rect(-1.4, 0.48, 0.6, 0.6), new Rect(-0.09, 0.48, 0.6, 0.6)),
					new IllustrationImage("colosseum_foreground_right.png", new Rect(1, 0.48, 0.6, 0.6), new Rect(0.48, 0.56, 0.6, 0.6)),
				]
			)
		},
		{
			WonderType.ChichenItza, new Illustration(
				Type: WonderType.ChichenItza,
				Title: "Chichen Itza",
				MainObject: "chichen_itza_chichen.png",
				Photo1: "chichen_itza_photo_1.jpg",
				Photo2: "chichen_itza_photo_2.jpg",
				Photo3: "chichen_itza_photo_3.jpg",
				Photo4: "chichen_itza_photo_4.jpg",
				Map: "chichen_itza_map.jpg",
				WonderButton: "chichen_itza_wonder_button.png",
				Flattened: "chichen_itza_flattened.jpg",
				ScaleX: 2.88,
				ScaleY: 2.88,
				MarginTop: 0,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#144f27"),
				SecondaryColor: Color.FromArgb("#e0cfb7"),
				BackgroundImages:
				[
					new IllustrationImage("chichen_itza_sun.png", new Rect(0.57, 0.3, 0.35, 0.4), new Rect(0.57, 0.20, 0.35, 0.4)),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, 0, 0.61, 0.6), new Rect(-0.05, 0, 0.61, 0.6), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(1.2, -0.1, 0.4, 0.4), new Rect(0.5, -0.1, 0.4, 0.4), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.25, 0.6, 0.6), new Rect(0, -0.25, 0.6, 0.6), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("chichen_itza_chichen.png", new Rect(-0.94, -0.94, 2.88, 2.88), new Rect(-0.94, -0.94, 2.88, 2.88)),
				MainObjectEditorialImage: new IllustrationImage("chichen_itza_chichen.png", new Rect(-0.94, -0.94, 2.88, 2.88), new Rect(0, 0, 0.5, 0.5)),
				ForegroundImages:
				[
					new IllustrationImage("chichen_itza_foreground_left.png", new Rect(-1, 0.20, 1.15, 1.15), new Rect(-0.46, 0.20, 1.15, 1.15)),
					new IllustrationImage("chichen_itza_foreground_right.png", new Rect(1, 0.45, 0.7, 0.7), new Rect(0.65, 0.45, 0.7, 0.7)),
					new IllustrationImage("chichen_itza_top_left.png", new Rect(-1, -0.55, 1.1, 1.1), new Rect(-0.4, -0.55, 1.1, 1.1)),
					new IllustrationImage("chichen_itza_top_right.png", new Rect(1, -0.5, 1.1, 1.1), new Rect(0.4, -0.5, 1.1, 1.1)),
				]
			)
		},
		{
			WonderType.ChristRedeemer, new Illustration(
				Type: WonderType.ChristRedeemer,
				Title: "Christ the Redeemer",
				MainObject: "christ_the_redeemer_redeemer.png",
				Photo1: "christ_the_redeemer_photo_1.jpg",
				Photo2: "christ_the_redeemer_photo_2.jpg",
				Photo3: "christ_the_redeemer_photo_3.jpg",
				Photo4: "christ_the_redeemer_photo_4.jpg",
				Map: "christ_redeemer_map.jpg",
				WonderButton: "christ_the_redeemer_wonder_button.png",
				Flattened: "christ_the_redeemer_flattened.jpg",
				ScaleX: 2.8,
				ScaleY: 2.8,
				MarginTop: 330,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#194d43"),
				SecondaryColor: Color.FromArgb("#eb7a65"),
				BackgroundImages:
				[
					new IllustrationImage("christ_the_redeemer_sun.png", new Rect(0.45, -0.1, 0.43, 0.43), new Rect(0.46, 0.02, 0.43, 0.43)),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, 0.13, 0.5, 0.5), new Rect(0.1, 0.13, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(1.2, 0.14, 0.34, 0.34), new Rect(-0.06, 0.14, 0.34, 0.34), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.05, 0.6, 0.6), new Rect(0.2, -0.05, 0.6, 0.6), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("christ_the_redeemer_redeemer.png", new Rect(-0.94, -0.72, 2.88, 2.88), new Rect(-0.94, -0.72, 2.88, 2.88)),
				MainObjectEditorialImage: new IllustrationImage("christ_the_redeemer_redeemer.png", new Rect(0, 0, 2.2, 2.2), new Rect(-0.6, 0.1, 2.2, 2.2)),
				ForegroundImages:
				[
					new IllustrationImage("christ_the_redeemer_foreground_left.png", new Rect(-1.4, -0.05, 1.64, 1.64), new Rect(-0.74, -0.05, 1.64, 1.64), Opacity: 0.8),
					new IllustrationImage("christ_the_redeemer_foreground_right.png", new Rect(1, 0.08, 1.6, 1.6), new Rect(0.13, 0.08, 1.6, 1.6)),
				]
			)
		},
		{
			WonderType.GreatWall, new Illustration(
				Type: WonderType.GreatWall,
				Title: "Great Wall of China",
				MainObject: "great_wall_of_china_great_wall.png",
				Photo1: "great_wall_of_china_photo_1.jpg",
				Photo2: "great_wall_of_china_photo_2.jpg",
				Photo3: "great_wall_of_china_photo_3.jpg",
				Photo4: "great_wall_of_china_photo_4.jpg",
				Map: "great_wall_map.jpg",
				WonderButton: "great_wall_of_china_wonder_button.png",
				Flattened: "great_wall_of_china_flattened.jpg",
				ScaleX: 1.2,
				ScaleY: 1.2,
				MarginTop: 40,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#612925"),
				SecondaryColor: Color.FromArgb("#8faa7b"),
				BackgroundImages:
				[
					new IllustrationImage("great_wall_of_china_sun.png", new Rect(0.16, 0.1, 0.34, 0.34), new Rect(0.16, -0.02, 0.34, 0.34)),
					new IllustrationImage("common_cloud_white.png", new Rect(0.5, -0.05, 0.5, 0.5), new Rect(-0.05, -0.05, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.8, 0.10, 0.6, 0.6), new Rect(-0.45, 0.10, 0.6, 0.6), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.2, -0.06, 0.34, 0.34), new Rect(0.18, -0.06, 0.34, 0.34), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("great_wall_of_china_great_wall.png", new Rect(-0.1, -0.07, 1.2, 1.2), new Rect(-0.1, -0.07, 1.2, 1.2)),
				MainObjectEditorialImage: new IllustrationImage("great_wall_of_china_great_wall.png", new Rect(0, 0, 2.88, 2.88), new Rect(0, 0, 2.88, 2.88)),
				ForegroundImages:
				[
					new IllustrationImage("great_wall_of_china_foreground_left.png", new Rect(-1.4, 0.33, 0.9, 0.9), new Rect(-0.27, 0.33, 0.9, 0.9)),
					new IllustrationImage("great_wall_of_china_foreground_right.png", new Rect(1, 0.16, 1.18, 1.18), new Rect(0.56, 0.16, 1.18, 1.18)),
				]
			)
		},
		{
			WonderType.MachuPicchu, new Illustration(
				Type: WonderType.MachuPicchu,
				Title: "Machu Picchu",
				MainObject: "machu_picchu_machu_picchu.png",
				Photo1: "machu_picchu_photo_1.jpg",
				Photo2: "machu_picchu_photo_2.jpg",
				Photo3: "machu_picchu_photo_3.jpg",
				Photo4: "machu_picchu_photo_4.jpg",
				Map: "machu_picchu_map.jpg",
				WonderButton: "machu_picchu_wonder_button.png",
				Flattened: "machu_picchu_flattened.jpg",
				ScaleX: 2.5,
				ScaleY: 2.5,
				MarginTop: 0,
				MarginLeft: -70,
				PrimaryColor: Color.FromArgb("#0b4161"),
				SecondaryColor: Color.FromArgb("#bfdace"),
				BackgroundImages:
				[
					new IllustrationImage("machu_picchu_sun.png", new Rect(0.56, 0.1, 0.34, 0.34), new Rect(0.56, -0.02, 0.34, 0.34)),
					new IllustrationImage("common_cloud_white.png", new Rect(0.5, -0.05, 0.5, 0.5), new Rect(0.3, -0.05, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.8, 0.10, 0.6, 0.6), new Rect(-0.45, 0.10, 0.6, 0.6), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.2, -0.06, 0.34, 0.34), new Rect(0.18, -0.06, 0.34, 0.34), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("machu_picchu_machu_picchu.png", new Rect(-1.04, -0.75, 2.9, 2.5), new Rect(-1.04, -0.75, 2.9, 2.5)),
				MainObjectEditorialImage: new IllustrationImage("machu_picchu_machu_picchu.png", new Rect(0, 0, 2.88, 2.88), new Rect(0, 0, 2.88, 2.88)),
				ForegroundImages:
				[
					new IllustrationImage("machu_picchu_foreground_back.png", new Rect(-0.18, 0.16, 1.46, 1.46), new Rect(-0.18, 0.12, 1.46, 1.46)),
					new IllustrationImage("machu_picchu_foreground_front.png", new Rect(-0.8, 0.46, 1.1, 1.1), new Rect(-0.5, 0.46, 1.1, 1.1)),
				]
			)
		},
		{
			WonderType.Petra, new Illustration(
				Type: WonderType.Petra,
				Title: "Petra",
				MainObject: "petra_petra.png",
				Photo1: "petra_photo_1.jpg",
				Photo2: "petra_photo_2.jpg",
				Photo3: "petra_photo_3.jpg",
				Photo4: "petra_photo_4.jpg",
				Map: "petra_map.jpg",
				WonderButton: "petra_wonder_button.png",
				Flattened: "petra_flattened.jpg",
				ScaleX: 2.3,
				ScaleY: 2.0,
				MarginTop: 0,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#424c98"),
				SecondaryColor: Color.FromArgb("#181a62"),
				BackgroundImages:
				[
					new IllustrationImage("petra_moon.png", new Rect(0.2, -0.2, 0.24, 0.24), new Rect(0.2, -0.08, 0.24, 0.24)),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.13, 0.5, 0.5), new Rect(0.1, -0.13, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(1.2, 0.14, 0.34, 0.34), new Rect(-0.06, 0.14, 0.34, 0.34), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.05, 0.6, 0.6), new Rect(0.2, -0.05, 0.6, 0.6), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("petra_petra.png", new Rect(-0.44, -0.50, 2.0, 2.0), new Rect(-0.44, -0.50, 2.0, 2.0)),
				MainObjectEditorialImage: new IllustrationImage("petra_petra.png", new Rect(0, 0, 2.88, 2.88), new Rect(0, 0, 2.88, 2.88)),
				ForegroundImages:
				[
					new IllustrationImage("petra_foreground_left.png", new Rect(-1.4, -0.04, 1, 1.1), new Rect(-0.4, -0.04, 1, 1.1)),
					new IllustrationImage("petra_foreground_right.png", new Rect(1, -0.04, 1, 1.1), new Rect(0.38, -0.04, 1, 1.1)),
				]
			)
		},
		{
			WonderType.PyramidsGiza, new Illustration(
				Type: WonderType.PyramidsGiza,
				Title: "Pyramids of Giza",
				MainObject: "pyramids_pyramids.png",
				Photo1: "pyramids_photo_1.jpg",
				Photo2: "pyramids_photo_2.jpg",
				Photo3: "pyramids_photo_3.jpg",
				Photo4: "pyramids_photo_4.jpg",
				Map: "pyramids_giza_map.jpg",
				WonderButton: "pyramids_wonder_button.png",
				Flattened: "pyramids_flattened.jpg",
				ScaleX: 1.8,
				ScaleY: 1.8,
				MarginTop: 0,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#14194c"),
				SecondaryColor: Color.FromArgb("#424c98"),
				BackgroundImages:
				[
					new IllustrationImage("pyramids_moon.png", new Rect(0.5, 0.2, 0.41, 0.41), new Rect(0.5, -0.0, 0.41, 0.41)),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.13, 0.5, 0.5), new Rect(0.1, -0.13, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(1.2, -0.14, 0.65, 0.65), new Rect(0, -0.14, 0.65, 0.65), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, 0.05, 0.5, 0.5), new Rect(0.2, 0.05, 0.5, 0.5), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("pyramids_pyramids.png", new Rect(-0.4, -0.4, 1.8, 1.8), new Rect(-0.4, -0.4, 1.8, 1.8)),
				MainObjectEditorialImage: new IllustrationImage("pyramids_pyramids.png", new Rect(0, 0, 2.88, 2.88), new Rect(0, 0, 2.88, 2.88)),
				ForegroundImages:
				[
					new IllustrationImage("pyramids_foreground_back.png", new Rect(0, 0.2, 1, 1), new Rect(0, 0.18, 1, 1)),
					new IllustrationImage("pyramids_foreground_front.png", new Rect(-0.18, 0.15, 1.4, 1.4), new Rect(-0.18, 0.1, 1.4, 1.4)),
				]
			)
		},
		{
			WonderType.TajMahal, new Illustration(
				Type: WonderType.TajMahal,
				Title: "Taj Mahal",
				MainObject: "taj_mahal_taj_mahal.png",
				Photo1: "taj_mahal_photo_1.jpg",
				Photo2: "taj_mahal_photo_2.jpg",
				Photo3: "taj_mahal_photo_3.jpg",
				Photo4: "taj_mahal_photo_4.jpg",
				Map: "taj_mahal_map.jpg",
				WonderButton: "taj_mahal_wonder_button.png",
				Flattened: "taj_mahal_flattened.jpg",
				ScaleX: 1.8,
				ScaleY: 1.9,
				MarginTop: 0,
				MarginLeft: 0,
				PrimaryColor: Color.FromArgb("#c86552"),
				SecondaryColor: Color.FromArgb("#612925"),
				BackgroundImages:
				[
					new IllustrationImage("taj_mahal_sun.png", new Rect(-0.4, 0.1, 0.5, 0.5), new Rect(-0.1, -0.14, 0.5, 0.5)),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, -0.13, 0.5, 0.5), new Rect(0.1, -0.13, 0.5, 0.5), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(1.2, -0.14, 0.65, 0.65), new Rect(0, -0.14, 0.65, 0.65), Opacity: 0.3),
					new IllustrationImage("common_cloud_white.png", new Rect(-0.5, 0.05, 0.5, 0.5), new Rect(0.2, 0.05, 0.5, 0.5), Opacity: 0.3),
				],
				MainObjectImage: new IllustrationImage("taj_mahal_taj_mahal.png", new Rect(-0.4, -0.466, 1.8, 1.9), new Rect(-0.4, -0.466, 1.8, 1.9)),
				MainObjectEditorialImage: new IllustrationImage("taj_mahal_taj_mahal.png", new Rect(0, 0, 2.88, 2.88), new Rect(0, 0, 2.88, 2.88)),
				ForegroundImages:
				[
					new IllustrationImage("taj_mahal_pool.png", new Rect(-0.18, 0.25, 1.4, 1.4), new Rect(-0.18, 0.2, 1.4, 1.4)),
					new IllustrationImage("taj_mahal_foreground_left.png", new Rect(-0.8, 0.31, 0.8, 0.8), new Rect(-0.35, 0.31, 0.8, 0.8)),
					new IllustrationImage("taj_mahal_foreground_right.png", new Rect(0.8, 0.24, 0.9, 0.9), new Rect(0.48, 0.24, 0.9, 0.9)),
				]
			)
		},
	};
}

record IllustrationImage(string Source, Rect InitialBounds, Rect FinalBounds, double Opacity = 1.0)
{
	public Rect GetFinalBounds(Size containerSize)
	{
		return new Rect(
			FinalBounds.X * containerSize.Width,
			FinalBounds.Y * containerSize.Height,
			FinalBounds.Width * containerSize.Width,
			FinalBounds.Height * containerSize.Height);
	}

	public Rect GetInitialBounds(Size containerSize)
	{
		return new Rect(
			InitialBounds.X * containerSize.Width,
			InitialBounds.Y * containerSize.Height,
			InitialBounds.Width * containerSize.Width,
			InitialBounds.Height * containerSize.Height);
	}
}
