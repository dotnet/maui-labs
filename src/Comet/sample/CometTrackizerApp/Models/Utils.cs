namespace CometTrackizerApp.Models;

public static class Utils
{
	public static string? GetDisplayName(this Enum enumValue)
	{
		var type = enumValue.GetType();
		var memberInfo = type.GetMember(enumValue.ToString());
		if (memberInfo.Length > 0)
		{
			var attributes = memberInfo[0].GetCustomAttributes(typeof(DisplayAttribute), false);
			if (attributes.Length > 0)
			{
				return ((DisplayAttribute)attributes[0]).Name;
			}
		}
		return enumValue.ToString();
	}
}
