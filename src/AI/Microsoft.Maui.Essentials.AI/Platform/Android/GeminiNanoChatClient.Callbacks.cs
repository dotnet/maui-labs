using System.Threading.Channels;
using AndroidNative = Com.Microsoft.Maui.Essentials.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

public sealed partial class GeminiNanoChatClient
{
	private sealed class CheckStatusCallback(CancellationToken cancellationToken)
		: Java.Lang.Object, AndroidNative.INativeCheckStatusCallback
	{
		private readonly TaskCompletionSource<AndroidNative.NativeModelStatus> _response =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task<AndroidNative.NativeModelStatus> Response => _response.Task;

		public void OnComplete(AndroidNative.NativeModelStatus status) =>
			_response.TrySetResult(status);

		public void OnError(AndroidNative.NativeChatError? error) =>
			_response.TrySetException(CreateNativeException(error, cancellationToken));
	}

	private sealed class DownloadCallback(ChannelWriter<GeminiNanoDownloadUpdate> writer, CancellationToken cancellationToken)
		: Java.Lang.Object, AndroidNative.INativeDownloadCallback
	{
		private readonly TaskCompletionSource _terminated =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task Terminated => _terminated.Task;

		public void OnStarted(long bytesToDownload) =>
			writer.TryWrite(new(GeminiNanoDownloadState.Started, bytesToDownload));

		public void OnProgress(long bytesDownloaded) =>
			writer.TryWrite(new(GeminiNanoDownloadState.Progress, bytesDownloaded));

		public void OnComplete()
		{
			writer.TryWrite(new(GeminiNanoDownloadState.Completed, 0));
			writer.TryComplete();
			_terminated.TrySetResult();
		}

		public void OnError(AndroidNative.NativeChatError? error)
		{
			writer.TryComplete(CreateNativeException(error, cancellationToken));
			_terminated.TrySetResult();
		}
	}

	private sealed class ResponseCallback(CancellationToken cancellationToken) :
		Java.Lang.Object,
		AndroidNative.INativeChatCallback
	{
		private readonly TaskCompletionSource<AndroidNative.NativeChatResponse> _response =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task<AndroidNative.NativeChatResponse> Response => _response.Task;

		// Non-streaming requests use only the terminal response callback.
		public void OnText(string? text, string modelName)
		{
		}

		public void OnThought(string? thought, string modelName)
		{
		}

		public void OnComplete(AndroidNative.NativeChatResponse? response)
		{
			if (response is null)
			{
				OnError(new AndroidNative.NativeChatError(
					"Gemini Nano returned no response.",
					0,
					null));
				return;
			}

			_response.TrySetResult(response);
		}

		public void OnError(AndroidNative.NativeChatError? error) =>
			_response.TrySetException(CreateNativeException(error, cancellationToken));
	}

	private sealed class StreamingResponseCallback(
		ChannelWriter<ChatResponseUpdate> writer,
		bool includeThoughts,
		CancellationToken cancellationToken) :
		Java.Lang.Object,
		AndroidNative.INativeChatCallback
	{
		private readonly TaskCompletionSource _terminated =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Task Terminated => _terminated.Task;

		public void OnText(string? text, string modelName)
		{
			if (string.IsNullOrEmpty(text))
				return;

			writer.TryWrite(new ChatResponseUpdate(ChatRole.Assistant, text)
			{
				ModelId = modelName,
			});
		}

		public void OnThought(string? thought, string modelName)
		{
			if (!includeThoughts || string.IsNullOrEmpty(thought))
				return;

			writer.TryWrite(new ChatResponseUpdate(
				ChatRole.Assistant,
				[new TextReasoningContent(thought)])
			{
				ModelId = modelName,
			});
		}

		public void OnComplete(AndroidNative.NativeChatResponse? response)
		{
			if (response is null)
			{
				OnError(new AndroidNative.NativeChatError(
					"Gemini Nano returned no response.",
					0,
					null));
				return;
			}

			try
			{
				writer.TryWrite(CreateFinalUpdate(response));
				writer.TryComplete();
			}
			finally
			{
				_terminated.TrySetResult();
			}
		}

		public void OnError(AndroidNative.NativeChatError? error)
		{
			try
			{
				writer.TryComplete(CreateNativeException(error, cancellationToken));
			}
			finally
			{
				_terminated.TrySetResult();
			}
		}
	}
}
