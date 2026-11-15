using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.Maui.Handlers.WPF
{
	/// <summary>
	/// The MAUI 11 HybridWebView message protocol spoken by <c>_framework/hybridwebview.js</c>,
	/// dispatched through the public <see cref="IHybridWebView"/> members.
	/// </summary>
	internal static class HybridWebViewProtocol
	{
		internal const string InvokeDotNetPath = "__hwvInvokeDotNet";
		internal const string InvokeTokenHeaderName = "X-Maui-Invoke-Token";
		internal const string InvokeTokenHeaderValue = "HybridWebView";
		internal const string RequestBodyHeaderName = "X-Maui-Request-Body";

		const string RawMessageType = "__RawMessage";

		/// <summary>
		/// Handles a web message posted by the HybridWebView script. WebView2 messages are URL-encoded
		/// as a whole (<c>&lt;type&gt;|&lt;content&gt;</c>), and raw-message content is URL-encoded again.
		/// </summary>
		/// <returns><see langword="true"/> when the message was recognized and dispatched.</returns>
		public static bool HandleWebMessage(IHybridWebView hybridWebView, string? webMessage)
		{
			if (string.IsNullOrEmpty(webMessage))
				return false;

			var message = Uri.UnescapeDataString(webMessage);
			var pipeIndex = message.IndexOf('|', StringComparison.Ordinal);
			if (pipeIndex == -1)
				return false;

			var messageType = message.Substring(0, pipeIndex);
			var messageContent = message.Substring(pipeIndex + 1);
			if (messageType != RawMessageType)
				return false;

			hybridWebView.RawMessageReceived(Uri.UnescapeDataString(messageContent));
			return true;
		}

		/// <summary>
		/// Invokes the .NET method described by an InvokeDotNet request body
		/// (<c>{"MethodName":"...","ParamValues":["&lt;json&gt;", ...]}</c>) through
		/// <see cref="IHybridWebView.Invoker"/>, and returns the JSON response the script expects.
		/// </summary>
		public static async Task<byte[]> InvokeDotNetAsync(IHybridWebView hybridWebView, string requestBody)
		{
			try
			{
				var (methodName, paramValues) = ParseInvokeRequest(requestBody);
				var result = await hybridWebView.Invoker.InvokeMethodAsync(methodName, paramValues);
				return CreateResult(result, null);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[HybridWebView] InvokeDotNet error: {ex.Message}");
				return CreateResult(null, ex);
			}
		}

		static (string MethodName, string[]? ParamValues) ParseInvokeRequest(string requestBody)
		{
			using var doc = JsonDocument.Parse(requestBody);
			var root = doc.RootElement;

			var methodName = root.TryGetProperty("MethodName", out var methodElement) && methodElement.ValueKind == JsonValueKind.String
				? methodElement.GetString()
				: null;
			if (string.IsNullOrEmpty(methodName))
				throw new InvalidOperationException("The invoke data did not provide a method name.");

			string[]? paramValues = null;
			if (root.TryGetProperty("ParamValues", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Array)
			{
				var values = new List<string>();
				foreach (var item in paramsElement.EnumerateArray())
					values.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText());
				paramValues = values.ToArray();
			}

			return (methodName, paramValues);
		}

		static byte[] CreateResult(string? jsonResult, Exception? error)
		{
			using var stream = new MemoryStream();
			using (var writer = new Utf8JsonWriter(stream))
			{
				writer.WriteStartObject();
				writer.WriteString("Result", jsonResult);
				writer.WriteBoolean("IsJson", jsonResult != null);
				writer.WriteBoolean("IsError", error != null);
				writer.WriteString("ErrorMessage", error?.Message);
				writer.WriteString("ErrorType", error?.GetType().Name);
				writer.WriteString("ErrorStackTrace", error?.StackTrace);
				writer.WriteEndObject();
			}
			return stream.ToArray();
		}
	}
}
