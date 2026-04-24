namespace CometTrackizerApp.Models;

public record Subscription(SubscriptionType Type, double MonthBill, DateOnly StartingDate);

public enum SubscriptionType
{
	Spotify,

	[Display(Name = "YouTube Premium")]
	YouTube,

	[Display(Name = "Microsoft OneDrive")]
	OneDrive,

	Netflix
}
