namespace CometMarvelousApp.Models;

public class Wonder
{
	public required WonderType Type { get; init; }
	public required string Title { get; init; }
	public required string SubTitle { get; init; }
	public required string RegionTitle { get; init; }
	public required string HistoryInfo1 { get; init; }
	public required string HistoryInfo2 { get; init; }
	public required string ConstructionInfo1 { get; init; }
	public required string ConstructionInfo2 { get; init; }
	public required string LocationInfo1 { get; init; }
	public required string LocationInfo2 { get; init; }
	public required string PullQuote1Top { get; init; }
	public required string PullQuote1Bottom { get; init; }
	public required string PullQuote1Author { get; init; }
	public required string PullQuote2 { get; init; }
	public required string PullQuote2Author { get; init; }
	public required string Callout1 { get; init; }
	public required string Callout2 { get; init; }
	public required string UnsplashCollectionId { get; init; }
	public required string VideoId { get; init; }
	public required string VideoCaption { get; init; }
	public required string MapCaption { get; init; }
	public IList<string>? ImageIds { get; init; }
	public IList<string>? Facts { get; init; }
	public required int StartYr { get; init; }
	public required int EndYr { get; init; }
	public required int ArtifactStartYr { get; init; }
	public required int ArtifactEndYr { get; init; }
	public required string ArtifactCulture { get; init; }
	public required string ArtifactGeolocation { get; init; }
	public required double Lat { get; init; }
	public required double Lng { get; init; }
	public required IList<string> HighlightArtifacts { get; init; }
	public required IList<string> HiddenArtifacts { get; init; }
	public required IDictionary<int, string> Events { get; init; }
	public IList<Search> SearchData { get; set; } = new List<Search>();
	public IList<string> SearchSuggestions { get; set; } = new List<string>();

	public static IReadOnlyDictionary<WonderType, Wonder> Config { get; } = new Dictionary<WonderType, Wonder>()
	{
		{
			WonderType.Colosseum, new Wonder
			{
				Type = WonderType.Colosseum,
				Title = Localization.ResourceManager.GetString("colosseumTitle").ThrowIfNull(),
				SubTitle = Localization.ResourceManager.GetString("colosseumSubTitle").ThrowIfNull(),
				RegionTitle = Localization.ResourceManager.GetString("colosseumRegionTitle").ThrowIfNull(),
				VideoId = "GXoEpNjgKzg",
				StartYr = 70,
				EndYr = 80,
				ArtifactStartYr = 0,
				ArtifactEndYr = 500,
				ArtifactCulture = Localization.ResourceManager.GetString("colosseumArtifactCulture").ThrowIfNull(),
				ArtifactGeolocation = Localization.ResourceManager.GetString("colosseumArtifactGeolocation").ThrowIfNull(),
				Lat = 41.890242126393495,
				Lng = 12.492349361871392,
				UnsplashCollectionId = "VPdti8Kjq9o",
				PullQuote1Top = Localization.ResourceManager.GetString("colosseumPullQuote1Top").ThrowIfNull(),
				PullQuote1Bottom = Localization.ResourceManager.GetString("colosseumPullQuote1Bottom").ThrowIfNull(),
				PullQuote1Author = "",
				PullQuote2 = Localization.ResourceManager.GetString("colosseumPullQuote2").ThrowIfNull(),
				PullQuote2Author = Localization.ResourceManager.GetString("colosseumPullQuote2Author").ThrowIfNull(),
				Callout1 = Localization.ResourceManager.GetString("colosseumCallout1").ThrowIfNull(),
				Callout2 = Localization.ResourceManager.GetString("colosseumCallout2").ThrowIfNull(),
				VideoCaption = Localization.ResourceManager.GetString("colosseumVideoCaption").ThrowIfNull(),
				MapCaption = Localization.ResourceManager.GetString("colosseumMapCaption").ThrowIfNull(),
				HistoryInfo1 = Localization.ResourceManager.GetString("colosseumHistoryInfo1").ThrowIfNull(),
				HistoryInfo2 = Localization.ResourceManager.GetString("colosseumHistoryInfo2").ThrowIfNull(),
				ConstructionInfo1 = Localization.ResourceManager.GetString("colosseumConstructionInfo1").ThrowIfNull(),
				ConstructionInfo2 = Localization.ResourceManager.GetString("colosseumConstructionInfo2").ThrowIfNull(),
				LocationInfo1 = Localization.ResourceManager.GetString("colosseumLocationInfo1").ThrowIfNull(),
				LocationInfo2 = Localization.ResourceManager.GetString("colosseumLocationInfo2").ThrowIfNull(),
				HighlightArtifacts = new List<string> {
					"251350",
					"255960",
					"247993",
					"250464",
					"251476",
					"255960",
				},
				HiddenArtifacts = new List<string> {
					"245376",
					"256570",
					"286136",
				},
				Events = new Dictionary<int, string>{
					[70] = "colosseum70ce",
					[82] = "colosseum82ce",
					[1140] = "colosseum1140ce",
					[1490] = "colosseum1490ce",
					[1829] = "colosseum1829ce",
					[1990] = "colosseum1990ce",
				}
			}
		},
	};
}

public class Search
{
	const string BaseImagePath = "https://images.metmuseum.org/CRDImages/";

	public required int Id { get; init; }
	public required int Year { get; init; }
	public required string Title { get; init; }
	public required string Keywords { get; init; }
	public required string ImagePath { get; init; }
	public required double AspectRatio { get; init; }

	public string ImageUrl => BaseImagePath + ImagePath;

	public Search(int year, int id, string title, string keywords, string imagePath, double aspectRatio = 0)
	{
		Year = year;
		Id = id;
		Title = title;
		Keywords = keywords;
		ImagePath = imagePath;
		AspectRatio = aspectRatio;
	}

	public string Write()
	{
		return $"SearchData({Year}, {Id}, {Title}, {Keywords}, {ImagePath}, {GetFormatedAspectRatio})";
	}

	private string GetFormatedAspectRatio()
	{
		return AspectRatio == 0 ? "" : AspectRatio.ToString("0.00");
	}
}
