namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Marks a method as a DevFlow Action — a named, discoverable shortcut that AI agents
/// and the <c>maui devflow invoke</c> CLI can call at runtime.
///
/// <para>
/// Use this to expose debug/test helpers (e.g., auto-login, seed data, navigate to a
/// deep screen) so that AI agents can discover and invoke them instead of manually
/// stepping through the UI.
/// </para>
///
/// <para>
/// Annotate parameters with <see cref="System.ComponentModel.DescriptionAttribute"/>
/// to provide AI-visible documentation for each parameter.
/// </para>
///
/// <example>
/// <code>
/// [DevFlowAction("login-test-user", Description = "Log in as the standard test account")]
/// public static async Task LoginTestUser(
///     [Description("Email address for the test account")] string email = "test@example.com",
///     [Description("Password for the test account")] string password = "password123")
/// {
///     await AuthService.LoginAsync(email, password);
/// }
/// </code>
/// </example>
/// </summary>
/// <remarks>
/// <para><b>Supported parameter types:</b></para>
/// <list type="bullet">
/// <item><description><c>string</c>, <c>bool</c></description></item>
/// <item><description><c>int</c>, <c>long</c>, <c>short</c>, <c>byte</c></description></item>
/// <item><description><c>float</c>, <c>double</c>, <c>decimal</c></description></item>
/// <item><description>Any <c>enum</c> type</description></item>
/// <item><description>Arrays or lists of the above: <c>string[]</c>, <c>List&lt;int&gt;</c>, etc.</description></item>
/// <item><description><c>Nullable&lt;T&gt;</c> of any supported value type</description></item>
/// </list>
///
/// <para>
/// Methods must be <c>public static</c>. Return type should be <c>void</c>,
/// <c>Task</c>, or <c>Task&lt;T&gt;</c> where T is a supported type or any
/// type whose <c>ToString()</c> produces a meaningful result.
/// </para>
///
/// <para>
/// The Roslyn analyzer <c>Microsoft.Maui.DevFlow.Analyzers</c> validates these
/// constraints at compile time when the analyzer is present.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DevFlowActionAttribute : Attribute
{
	/// <summary>
	/// The unique action name used to invoke this method via DevFlow tooling.
	/// Use kebab-case (e.g., "login-test-user", "seed-catalog").
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// A human-readable description of what this action does.
	/// AI agents see this when discovering available actions.
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Creates a new DevFlow Action attribute.
	/// </summary>
	/// <param name="name">
	/// Unique action name in kebab-case (e.g., "login-test-user").
	/// </param>
	public DevFlowActionAttribute(string name)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
	}
}
