using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IAIAdviceAdapter
{
    bool IsConfigured { get; }
    Task<AIAdviceResponseDto> GetShotAdviceAsync(AIAdviceRequestDto request, CancellationToken cancellationToken = default);
    Task<string?> GetPassiveInsightAsync(AIAdviceRequestDto request, CancellationToken cancellationToken = default);
    Task<AIRecommendationDto> GetBeanRecommendationAsync(BeanRecommendationContextDto context, CancellationToken cancellationToken = default);
}

public interface IImagePickerService
{
    bool IsPickerSupported { get; }
    Task<Stream?> PickImageAsync();
}

public interface IImageProcessingService
{
    Task<ImageValidationResult> ValidateImageAsync(Stream imageStream);
    Task<string> SaveImageAsync(Stream imageStream, string filename);
    Task<bool> DeleteImageAsync(string filename);
    string GetImagePath(string filename);
    bool ImageExists(string filename);
    Task<MemoryStream?> DownsampleAsync(Stream imageStream, int maxDimension, int quality);
}

public interface ISpeechRecognitionService
{
    SpeechRecognitionState State { get; }
    event EventHandler<SpeechRecognitionState>? StateChanged;
    event EventHandler<string>? PartialResultReceived;
    Task<bool> IsAvailableAsync();
    Task<bool> RequestPermissionsAsync();
    Task<SpeechRecognitionResultDto> StartListeningAsync(CancellationToken cancellationToken = default);
    Task StopListeningAsync();
}

public interface IVisionService
{
    Task<VisionAnalysisResult> AnalyzeImageAsync(Stream imageStream, string userQuestion, CancellationToken cancellationToken = default);
    Task<BeanLabelExtraction> ExtractBeanLabelAsync(Stream imageStream, CancellationToken cancellationToken = default);
    Task<PhotoWorkflowAnalysis> ClassifyPhotoAsync(Stream imageStream, CancellationToken cancellationToken = default);
    Task<PersonIdentificationResult> IdentifyPersonFromPhotoAsync(
        byte[] targetPhoto,
        IReadOnlyList<PersonIdentificationCandidate> candidates,
        CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync();
}

public sealed record ImageValidationResult(bool IsValid, string? ErrorMessage)
{
    public static ImageValidationResult Valid() => new(true, null);
    public static ImageValidationResult Invalid(string errorMessage) => new(false, errorMessage);
}
