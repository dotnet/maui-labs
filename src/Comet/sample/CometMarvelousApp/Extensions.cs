using System.Diagnostics.CodeAnalysis;

namespace CometMarvelousApp;

static class Extensions
{
	[return: NotNull]
	public static T ThrowIfNull<T>(this T? value) where T : class
		=> value ?? throw new ArgumentNullException(nameof(value));
}
