using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class PreferencesTools
{
	[McpServerTool(Name = "maui_preferences_list"), Description("List all known preference keys from the app's key-value store.")]
	public static async Task<string> ListPreferences(
		McpAgentSession session,
		[Description("Shared preferences name (optional, for shared containers)")] string? sharedName = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPreferencesAsync(sharedName);
		return result.ValueKind == JsonValueKind.Undefined ? "No preferences found." : result.ToString();
	}

	[McpServerTool(Name = "maui_preferences_get"), Description("Get a preference value by key from the app's key-value store.")]
	public static async Task<string> GetPreference(
		McpAgentSession session,
		[Description("Preference key to retrieve")] string key,
		[Description("Value type: string, int, bool, double, float, long, datetime (default: string)")] string? type = null,
		[Description("Shared preferences name (optional)")] string? sharedName = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPreferenceAsync(key, type, sharedName);
		return result.ValueKind == JsonValueKind.Undefined ? $"Preference '{key}' not found." : result.ToString();
	}

	[McpServerTool(Name = "maui_preferences_set"), Description("Set a preference value in the app's key-value store.")]
	public static async Task<string> SetPreference(
		McpAgentSession session,
		[Description("Preference key")] string key,
		[Description("Value to store")] string value,
		[Description("Value type: string, int, bool, double, float, long, datetime (default: string)")] string? type = null,
		[Description("Shared preferences name (optional)")] string? sharedName = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.SetPreferenceAsync(key, value, type, sharedName);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to set preference '{key}'." : result.ToString();
	}

	[McpServerTool(Name = "maui_preferences_delete"), Description("Remove a preference by key from the app's key-value store.")]
	public static async Task<string> DeletePreference(
		McpAgentSession session,
		[Description("Preference key to remove")] string key,
		[Description("Shared preferences name (optional)")] string? sharedName = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.DeletePreferenceAsync(key, sharedName);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to delete preference '{key}'." : result.ToString();
	}

	[McpServerTool(Name = "maui_preferences_clear"), Description("Clear all preferences from the app's key-value store.")]
	public static async Task<string> ClearPreferences(
		McpAgentSession session,
		[Description("Shared preferences name (optional)")] string? sharedName = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ClearPreferencesAsync(sharedName);
		return success ? "Preferences cleared." : "Failed to clear preferences.";
	}

	[McpServerTool(Name = "maui_secure_storage_get"), Description("Get a value from the app's encrypted secure storage.")]
	public static async Task<string> GetSecureStorage(
		McpAgentSession session,
		[Description("Secure storage key to retrieve")] string key,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetSecureStorageAsync(key);
		return result.ValueKind == JsonValueKind.Undefined ? $"Secure storage key '{key}' not found." : result.ToString();
	}

	[McpServerTool(Name = "maui_secure_storage_set"), Description("Set a value in the app's encrypted secure storage.")]
	public static async Task<string> SetSecureStorage(
		McpAgentSession session,
		[Description("Secure storage key")] string key,
		[Description("Value to store")] string value,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.SetSecureStorageAsync(key, value);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to set secure storage key '{key}'." : result.ToString();
	}

	[McpServerTool(Name = "maui_secure_storage_delete"), Description("Remove an entry from the app's encrypted secure storage.")]
	public static async Task<string> DeleteSecureStorage(
		McpAgentSession session,
		[Description("Secure storage key to remove")] string key,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.DeleteSecureStorageAsync(key);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to delete secure storage key '{key}'." : result.ToString();
	}

	[McpServerTool(Name = "maui_secure_storage_clear"), Description("Clear all entries from the app's encrypted secure storage.")]
	public static async Task<string> ClearSecureStorage(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ClearSecureStorageAsync();
		return success ? "Secure storage cleared." : "Failed to clear secure storage.";
	}
}
