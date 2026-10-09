using System.Diagnostics;
using System.Reflection;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

internal static class NativeElementDiagnosticsBridge
{
	static readonly DiagnosticListener s_listener = new("Microsoft.Maui.NativeElements");
	static readonly MethodInfo? s_register = ResolveMethod("Register", parameterCount: 4);
	static readonly MethodInfo? s_unregister = ResolveMethod("Unregister", parameterCount: 1);

	public static void Register(object owner, object nativeElement, string role)
	{
		if (s_register is not null)
		{
			s_register.Invoke(null, new object?[] { owner, nativeElement, role, null });
			return;
		}

		const string eventName = "Microsoft.Maui.NativeElements.Registered.v1";
		if (s_listener.IsEnabled(eventName))
			s_listener.Write(eventName, new object?[] { 1, owner, nativeElement, role, null });
	}

	public static void Unregister(object? nativeElement)
	{
		if (nativeElement is null)
			return;

		if (s_unregister is not null)
		{
			s_unregister.Invoke(null, new[] { nativeElement });
			return;
		}

		const string eventName = "Microsoft.Maui.NativeElements.Unregistered.v1";
		if (s_listener.IsEnabled(eventName))
			s_listener.Write(eventName, new object?[] { 1, nativeElement });
	}

	static MethodInfo? ResolveMethod(string name, int parameterCount)
		=> typeof(IMauiContext).Assembly
			.GetType("Microsoft.Maui.Diagnostics.NativeElementDiagnostics")
			?.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.FirstOrDefault(method =>
				method.Name == name &&
				method.GetParameters().Length == parameterCount);
}
