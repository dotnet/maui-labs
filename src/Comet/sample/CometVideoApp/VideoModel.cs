namespace CometVideoApp;

public class VideoModel
{
	public required string Title { get; set; }
	public required string Creator { get; set; }
	public required string ThumbnailColor { get; set; }
	public required string Description { get; set; }
	public int Likes { get; set; }
	public int Comments { get; set; }
	public int Shares { get; set; }
	public bool IsLiked { get; set; }

	public static VideoModel[] All { get; } =
	{
		new VideoModel
		{
			Title = "Sunset Timelapse",
			Creator = "@nature_films",
			ThumbnailColor = "#E65100",
			Description = "Golden hour in the mountains",
			Likes = 12_400,
			Comments = 342,
			Shares = 89
		},
		new VideoModel
		{
			Title = "Street Dance Battle",
			Creator = "@dance_crew",
			ThumbnailColor = "#4A148C",
			Description = "When the beat drops #dance #freestyle",
			Likes = 45_200,
			Comments = 1_203,
			Shares = 567
		},
		new VideoModel
		{
			Title = "Cooking Pasta",
			Creator = "@chef_marco",
			ThumbnailColor = "#1B5E20",
			Description = "The secret to perfect carbonara",
			Likes = 8_900,
			Comments = 455,
			Shares = 234
		},
		new VideoModel
		{
			Title = "Cat vs Cucumber",
			Creator = "@funny_pets",
			ThumbnailColor = "#0D47A1",
			Description = "He did NOT see that coming",
			Likes = 234_000,
			Comments = 5_670,
			Shares = 12_300
		},
		new VideoModel
		{
			Title = "DIY Room Makeover",
			Creator = "@home_hacks",
			ThumbnailColor = "#880E4F",
			Description = "Budget room transformation #diy #home",
			Likes = 67_800,
			Comments = 890,
			Shares = 445
		},
		new VideoModel
		{
			Title = "Ocean Waves ASMR",
			Creator = "@calm_vibes",
			ThumbnailColor = "#006064",
			Description = "Fall asleep in 5 minutes #asmr #sleep",
			Likes = 19_300,
			Comments = 210,
			Shares = 156
		},
		new VideoModel
		{
			Title = "Skateboard Tricks",
			Creator = "@sk8_life",
			ThumbnailColor = "#BF360C",
			Description = "Nailed the kickflip after 100 tries",
			Likes = 56_700,
			Comments = 2_340,
			Shares = 890
		},
		new VideoModel
		{
			Title = "Coding in 60 Seconds",
			Creator = "@dev_shorts",
			ThumbnailColor = "#1A237E",
			Description = "Build a todo app with .NET MAUI #coding",
			Likes = 3_200,
			Comments = 178,
			Shares = 67
		},
	};

	public static string FormatCount(int count) => count switch
	{
		>= 1_000_000 => $"{count / 1_000_000.0:F1}M",
		>= 1_000 => $"{count / 1_000.0:F1}K",
		_ => count.ToString()
	};
}
