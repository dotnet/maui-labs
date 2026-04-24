namespace CometBaristaNotes;

public static class ServiceHelper
{
	public static IServiceProvider? Services { get; set; }

	public static T? GetService<T>() where T : class
	{
		if (Services == null)
			return null;

		return Services.GetService(typeof(T)) as T;
	}
}
