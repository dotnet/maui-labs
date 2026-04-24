namespace CometMarvelousApp.Models;

static class WonderTypeExtensions
{
	public static WonderType Next(this WonderType wonderType)
		=> wonderType == WonderType.TajMahal ? WonderType.ChichenItza : (WonderType)((int)wonderType + 1);

	public static WonderType Previous(this WonderType wonderType)
		=> wonderType == WonderType.ChichenItza ? WonderType.TajMahal : (WonderType)((int)wonderType - 1);

	public static bool IsPreviousOf(this WonderType wonderType, WonderType nextWonderType)
		=> nextWonderType.Previous() == wonderType;

	public static bool IsNextOf(this WonderType wonderType, WonderType prevWonderType)
		=> prevWonderType.Next() == wonderType;
}
