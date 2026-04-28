using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class FileTools
{
	[McpServerTool(Name = "maui_storage_roots"), Description("List file storage roots advertised by the app. Use this before specifying a non-default root.")]
	public static async Task<string> ListStorageRoots(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.ListStorageRootsAsync();
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list storage roots." : result.ToString();
	}

	[McpServerTool(Name = "maui_files_list"), Description("List files and directories under an advertised app storage root. Optionally specify a subdirectory path.")]
	public static async Task<string> ListFiles(
		McpAgentSession session,
		[Description("Subdirectory path relative to the selected storage root (optional, lists root if omitted)")] string? path = null,
		[Description("Storage root id (default: appData; call maui_storage_roots to discover supported roots)")] string? root = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.ListFilesAsync(path, root);
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list files." : result.ToString();
	}

	[McpServerTool(Name = "maui_files_download"), Description("Download a file from an advertised app storage root. Returns the file content as base64.")]
	public static async Task<string> DownloadFile(
		McpAgentSession session,
		[Description("File path relative to the selected storage root")] string path,
		[Description("Storage root id (default: appData; call maui_storage_roots to discover supported roots)")] string? root = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.DownloadFileAsync(path, root);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to download file '{path}'." : result.ToString();
	}

	[McpServerTool(Name = "maui_files_upload"), Description("Upload a file to an advertised app storage root. Content must be base64-encoded. Parent directories are created automatically.")]
	public static async Task<string> UploadFile(
		McpAgentSession session,
		[Description("File path relative to the selected storage root")] string path,
		[Description("File content encoded as base64")] string contentBase64,
		[Description("Storage root id (default: appData; call maui_storage_roots to discover supported roots)")] string? root = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.UploadFileAsync(path, contentBase64, root);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to upload file '{path}'." : result.ToString();
	}

	[McpServerTool(Name = "maui_files_delete"), Description("Delete a file from an advertised app storage root.")]
	public static async Task<string> DeleteFile(
		McpAgentSession session,
		[Description("File path relative to the selected storage root")] string path,
		[Description("Storage root id (default: appData; call maui_storage_roots to discover supported roots)")] string? root = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.DeleteFileAsync(path, root);
		return success ? $"File '{path}' deleted." : $"Failed to delete file '{path}'.";
	}
}
