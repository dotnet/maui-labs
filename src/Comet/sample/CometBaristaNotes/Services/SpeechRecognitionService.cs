using CometBaristaNotes.Services.DTOs;
using CommunityToolkit.Maui.Media;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace CometBaristaNotes.Services;

/// <summary>
/// Speech recognition service using CommunityToolkit.Maui for on-device processing.
/// State machine: Idle → Listening → Processing → Error
/// </summary>
public class SpeechRecognitionService : ISpeechRecognitionService
{
	private readonly ISpeechToText _speechToText;
	private readonly ILogger<SpeechRecognitionService> _logger;
	private SpeechRecognitionState _state = SpeechRecognitionState.Idle;
	private CancellationTokenSource? _currentCts;
	private TaskCompletionSource<SpeechRecognitionResultDto>? _recognitionTcs;

	public SpeechRecognitionState State
	{
		get => _state;
		private set
		{
			if (_state != value)
			{
				_state = value;
				StateChanged?.Invoke(this, value);
			}
		}
	}

	public event EventHandler<SpeechRecognitionState>? StateChanged;
	public event EventHandler<string>? PartialResultReceived;

	public SpeechRecognitionService(ISpeechToText speechToText, ILogger<SpeechRecognitionService> logger)
	{
		_speechToText = speechToText;
		_logger = logger;

		// Subscribe to completion event once
		_speechToText.RecognitionResultCompleted += OnRecognitionResultCompleted;
	}

	public Task<bool> IsAvailableAsync()
	{
		return Task.FromResult(true);
	}

	public async Task<bool> RequestPermissionsAsync()
	{
		try
		{
			_logger.LogDebug("Checking speech recognition permission status");

			var micStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
			var speechStatus = await Permissions.CheckStatusAsync<Permissions.Speech>();

			_logger.LogInformation("Permission status: Mic={MicStatus}, Speech={SpeechStatus}",
				micStatus, speechStatus);

			if (micStatus == PermissionStatus.Granted && speechStatus == PermissionStatus.Granted)
				return true;

			// If not determined, let StartListenAsync trigger the permission prompt
			if (micStatus == PermissionStatus.Unknown || speechStatus == PermissionStatus.Unknown)
			{
				_logger.LogDebug("Permissions undetermined, will prompt on StartListenAsync");
				return true;
			}

			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error checking speech recognition permissions");
			return true;
		}
	}

	public async Task<SpeechRecognitionResultDto> StartListeningAsync(CancellationToken cancellationToken = default)
	{
		if (State == SpeechRecognitionState.Listening)
		{
			_logger.LogWarning("Already listening, ignoring start request");
			return new SpeechRecognitionResultDto
			{
				Success = false,
				ErrorMessage = "Already listening"
			};
		}

		_currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_recognitionTcs = new TaskCompletionSource<SpeechRecognitionResultDto>();
		State = SpeechRecognitionState.Listening;

		// 60-second safety timeout
		using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
		using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
			_currentCts.Token, timeoutCts.Token);

		try
		{
			_logger.LogDebug("Starting speech recognition");

			_speechToText.RecognitionResultUpdated += OnRecognitionResultUpdated;

			var options = new SpeechToTextOptions
			{
				Culture = CultureInfo.CurrentCulture,
				ShouldReportPartialResults = true
			};

			await _speechToText.StartListenAsync(options, combinedCts.Token);

			var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60), combinedCts.Token);
			var completedTask = await Task.WhenAny(_recognitionTcs.Task, timeoutTask);

			if (completedTask == timeoutTask)
			{
				_logger.LogWarning("Speech recognition timed out after 60 seconds");
				await StopListeningAsync();
				return new SpeechRecognitionResultDto
				{
					Success = false,
					ErrorMessage = "Listening timed out. Please try again."
				};
			}

			return await _recognitionTcs.Task;
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("Speech recognition cancelled");
			State = SpeechRecognitionState.Idle;
			return new SpeechRecognitionResultDto
			{
				Success = false,
				ErrorMessage = "Cancelled"
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error during speech recognition");
			State = SpeechRecognitionState.Error;
			return new SpeechRecognitionResultDto
			{
				Success = false,
				ErrorMessage = ex.Message
			};
		}
		finally
		{
			_speechToText.RecognitionResultUpdated -= OnRecognitionResultUpdated;
			_currentCts?.Dispose();
			_currentCts = null;
		}
	}

	public async Task StopListeningAsync()
	{
		_logger.LogDebug("Stopping speech recognition");

		try
		{
			await _speechToText.StopListenAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Error stopping speech recognition");
		}

		State = SpeechRecognitionState.Idle;
	}

	private void OnRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs args)
	{
		_logger.LogDebug("Partial result: {Text}", args.RecognitionResult);
		PartialResultReceived?.Invoke(this, args.RecognitionResult);
	}

	private void OnRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs args)
	{
		State = SpeechRecognitionState.Processing;

		var result = args.RecognitionResult;
		_logger.LogInformation("Recognition completed: Successful={IsSuccessful}", result.IsSuccessful);
		_logger.LogInformation("Final result text: {Text}", result.Text);

		if (result.IsSuccessful && !string.IsNullOrEmpty(result.Text))
		{
			_logger.LogInformation("Speech recognition successful: {Text}", result.Text);
			State = SpeechRecognitionState.Idle;
			_recognitionTcs?.TrySetResult(new SpeechRecognitionResultDto
			{
				Success = true,
				Transcript = result.Text,
				Confidence = 1.0
			});
		}
		else
		{
			var errorMessage = result.Exception?.Message ?? "No speech recognized";
			_logger.LogWarning("Speech recognition completed but failed: {Error}", errorMessage);
			State = SpeechRecognitionState.Error;
			_recognitionTcs?.TrySetResult(new SpeechRecognitionResultDto
			{
				Success = false,
				ErrorMessage = errorMessage
			});
		}
	}
}
