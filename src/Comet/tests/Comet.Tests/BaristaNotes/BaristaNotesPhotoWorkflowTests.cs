#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometSamples.BaristaNotes.Components;
using Xunit;

namespace Comet.Tests.BaristaNotes.Domain;

public sealed class BaristaNotesPhotoWorkflowTests
{
    static readonly PhotoAsset CameraPhoto =
        new([1, 2, 3], "image/jpeg", "camera.jpg", PhotoSource.Camera);

    [Fact]
    public void AppWorkflowOwners_SourceDoesNotDeclarePhotoByteCaches()
    {
        var root = FindCometRoot();
        if (root is null)
            return;

        var callbacks = System.IO.File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Services/AppPhotoWorkflowCallbacks.cs"));
        var app = System.IO.File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/BaristaNotesApp.cs"));
        var profilePage = System.IO.File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/Shared/BaristaNotes/Pages/ProfileDetailPage.cs"));

        Assert.DoesNotContain("_lastPhoto", callbacks);
        Assert.DoesNotContain("LastPhoto", callbacks);
        Assert.DoesNotContain("StagedPhotoBytes", app);
        Assert.DoesNotContain("Convert.ToBase64String", callbacks);
        Assert.DoesNotContain("Convert.ToBase64String", app);
        Assert.DoesNotContain("Convert.ToBase64String", profilePage);
        Assert.Contains("PhotoByteOwnership? _stagedAvatar", profilePage);
        Assert.Contains("void ReleaseStagedPhoto()", profilePage);
        Assert.Contains("new(\"profile_cancel\", \"CANCEL\", Cancel, enabled: !IsOperationActive)", profilePage);
        Assert.Contains("ReleaseStagedPhoto();", profilePage);
        Assert.Contains("_navigation.OpenProfileFromPhoto(photo.Bytes)", callbacks);
        Assert.Contains(
            "new ProfileDetailPage(null, stagedAvatarBytes: avatarBytes)",
            app);
    }

    [Fact]
    public void IOSPhotoService_RejectsSimulatorCameraBeforeAuthorizationOrPresentation()
    {
        var root = FindCometRoot();
        if (root is null)
            return;

        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            root,
            "sample/CometSwiftUIProbe/BaristaNotesPhotoService.cs"));
        var simulatorGuard = source.IndexOf(
            "if (Runtime.Arch == Arch.SIMULATOR)",
            StringComparison.Ordinal);
        var permissionRequest = source.IndexOf(
            "AVCaptureDevice.GetAuthorizationStatus",
            StringComparison.Ordinal);
        var pickerPresentation = source.IndexOf(
            "StartPickerAsync(",
            simulatorGuard,
            StringComparison.Ordinal);

        Assert.True(simulatorGuard >= 0);
        Assert.True(permissionRequest > simulatorGuard);
        Assert.True(pickerPresentation > simulatorGuard);
        Assert.Contains(
            "\"Camera capture is not available in the iOS simulator.\"",
            source);
        Assert.Contains(
            "Runtime.Arch != Arch.SIMULATOR",
            source);
    }

    [Fact]
    public void PhotoByteOwnership_Dispose_ReleasesDestinationBytes()
    {
        var ownership = new PhotoByteOwnership([19, 27, 83, 41]);
        Assert.True(ownership.HasBytes);

        ownership.Dispose();

        Assert.False(ownership.HasBytes);
        Assert.Throws<ObjectDisposedException>(() => ownership.OpenRead());
    }

    [Fact]
    public async Task RunCameraAsync_UserCancels_NotifiesCancellation()
    {
        var photos = new FakePhotoService
        {
            CaptureResults = new Queue<PhotoOperationResult>(
                [PhotoOperationResult.Cancelled()]),
        };
        var callbacks = new RecordingCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(
            photos,
            new FakeVisionAnalyzer(),
            callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Cancelled, outcome);
        Assert.Equal(PhotoWorkflowCancellation.CaptureCancelled, callbacks.Cancellation);
        Assert.Empty(callbacks.Routes);
    }

    public static IEnumerable<object[]> AcquisitionOutcomes()
    {
        foreach (var source in new[] { PhotoSource.Camera, PhotoSource.Gallery })
        {
            yield return
            [
                source,
                PhotoOperationResult.Cancelled(),
                PhotoWorkflowOutcome.Cancelled,
                source == PhotoSource.Camera
                    ? PhotoWorkflowCancellation.CaptureCancelled
                    : PhotoWorkflowCancellation.GalleryCancelled,
                null!,
            ];
            yield return
            [
                source,
                PhotoOperationResult.Unavailable("unavailable", "No provider."),
                PhotoWorkflowOutcome.Error,
                null!,
                PhotoOperationStatus.Unavailable,
            ];
            yield return
            [
                source,
                PhotoOperationResult.PermissionDenied("Permission denied."),
                PhotoWorkflowOutcome.Error,
                null!,
                PhotoOperationStatus.PermissionDenied,
            ];
            yield return
            [
                source,
                PhotoOperationResult.Error("read_failed", "Read failed."),
                PhotoWorkflowOutcome.Error,
                null!,
                PhotoOperationStatus.Error,
            ];
        }
    }

    [Theory]
    [MemberData(nameof(AcquisitionOutcomes))]
    public async Task RunAsync_AcquisitionOutcome_PublishesExplicitState(
        PhotoSource source,
        PhotoOperationResult result,
        PhotoWorkflowOutcome expectedOutcome,
        PhotoWorkflowCancellation? expectedCancellation,
        PhotoOperationStatus? expectedErrorStatus)
    {
        var photos = new FakePhotoService();
        if (source == PhotoSource.Camera)
            photos.CaptureResults.Enqueue(result);
        else
            photos.PickResults.Enqueue(result);
        var callbacks = new RecordingCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(
            photos,
            new FakeVisionAnalyzer(),
            callbacks);

        var outcome = source == PhotoSource.Camera
            ? await coordinator.RunCameraAsync()
            : await coordinator.RunGalleryAsync();

        Assert.Equal(expectedOutcome, outcome);
        Assert.Equal(expectedCancellation, callbacks.Cancellation);
        Assert.Equal(expectedErrorStatus, callbacks.Error?.Status);
        if (callbacks.Error is not null)
            Assert.Equal(source, callbacks.Error.Source);
    }

    [Theory]
    [InlineData(PhotoSource.Camera, PhotoOperationStatus.Cancelled, "Camera cancelled.")]
    [InlineData(PhotoSource.Gallery, PhotoOperationStatus.Cancelled, "Gallery cancelled.")]
    [InlineData(PhotoSource.Camera, PhotoOperationStatus.Unavailable, "Camera unavailable")]
    [InlineData(PhotoSource.Gallery, PhotoOperationStatus.PermissionDenied, "Gallery permission denied")]
    [InlineData(PhotoSource.Camera, PhotoOperationStatus.Error, "Camera error")]
    public void PhotoOperationFeedback_UsesExplicitSourceAndOutcome(
        PhotoSource source,
        PhotoOperationStatus status,
        string expected)
    {
        var result = status switch
        {
            PhotoOperationStatus.Cancelled => PhotoOperationResult.Cancelled(),
            PhotoOperationStatus.Unavailable => PhotoOperationResult.Unavailable("unavailable", "Not supported."),
            PhotoOperationStatus.PermissionDenied => PhotoOperationResult.PermissionDenied("Access denied."),
            _ => PhotoOperationResult.Error("failed", "Something failed."),
        };

        var feedback = PhotoOperationFeedback.FromResult(source, result);

        Assert.Equal(status, feedback.Status);
        Assert.Contains(expected, feedback.Message);
    }

    [Fact]
    public async Task RunCameraAsync_RetakeThenProfile_CapturesAgainAndRoutesProfile()
    {
        var photos = new FakePhotoService
        {
            CaptureResults = new Queue<PhotoOperationResult>(
            [
                PhotoOperationResult.Success(CameraPhoto),
                PhotoOperationResult.Success(CameraPhoto),
            ]),
        };
        var vision = new FakeVisionAnalyzer
        {
            Classifications = new Queue<PhotoClassificationResult>(
            [
                Classification(PhotoWorkflowIntent.Unknown, isObvious: false),
                Classification(PhotoWorkflowIntent.Profile, isObvious: true),
            ]),
        };
        var callbacks = new RecordingCallbacks
        {
            Choices = new Queue<PhotoIntentChoice>([PhotoIntentChoice.Retake]),
        };
        var coordinator = new PhotoWorkflowCoordinator(photos, vision, callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Profile, outcome);
        Assert.Equal(2, photos.CaptureCount);
        Assert.Equal(["profile"], callbacks.Routes);
        Assert.Equal(2, callbacks.VisionEvents.Count);
        Assert.False(callbacks.IsBusy);
    }

    [Fact]
    public async Task RunGalleryAsync_RoomVisionUnavailable_ReportsExplicitError()
    {
        var photos = new FakePhotoService
        {
            PickResults = new Queue<PhotoOperationResult>(
                [PhotoOperationResult.Success(
                    CameraPhoto with { Source = PhotoSource.Gallery })]),
        };
        var vision = new FakeVisionAnalyzer
        {
            Classifications = new Queue<PhotoClassificationResult>(
                [Classification(PhotoWorkflowIntent.Room, isObvious: true)]),
            RoomResult = new(
                VisionRequestStatus.Unavailable,
                null,
                "Vision is not configured."),
        };
        var callbacks = new RecordingCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, vision, callbacks);

        var outcome = await coordinator.RunGalleryAsync();

        Assert.Equal(PhotoWorkflowOutcome.Error, outcome);
        Assert.Equal("vision_unavailable", callbacks.Error?.Code);
        Assert.Contains("not configured", callbacks.Error?.Message);
        Assert.Empty(callbacks.Routes);
    }

    [Fact]
    public async Task RunCameraAsync_ClassificationCancelled_NotifiesRequestCancellation()
    {
        var photos = new FakePhotoService
        {
            CaptureResults = new Queue<PhotoOperationResult>(
                [PhotoOperationResult.Success(CameraPhoto)]),
        };
        var vision = new FakeVisionAnalyzer
        {
            Classifications = new Queue<PhotoClassificationResult>(
            [
                new(
                    VisionRequestStatus.Cancelled,
                    new PhotoWorkflowAnalysis(
                        false,
                        PhotoWorkflowIntent.Unknown,
                        false,
                        null,
                        "Vision request was cancelled."),
                    null,
                    "Vision request was cancelled."),
            ]),
        };
        var callbacks = new RecordingCallbacks();
        var coordinator = new PhotoWorkflowCoordinator(photos, vision, callbacks);

        var outcome = await coordinator.RunCameraAsync();

        Assert.Equal(PhotoWorkflowOutcome.Cancelled, outcome);
        Assert.Equal(PhotoWorkflowCancellation.RequestCancelled, callbacks.Cancellation);
        Assert.Empty(callbacks.Routes);
    }

    [Fact]
    public async Task AzureAnalyzer_MissingConfiguration_ReturnsUnavailableWithoutHttpCall()
    {
        var handler = new StubHandler(
            _ => throw new InvalidOperationException("HTTP must not be called."));
        var analyzer = new AzureOpenAiVisionAnalyzer(
            new BaristaVisionConfiguration(null, null),
            new HttpClient(handler));

        var result = await analyzer.ClassifyPhotoAsync(CameraPhoto);

        Assert.Equal(VisionAvailabilityState.NotConfigured, analyzer.Availability.State);
        Assert.Equal(VisionRequestStatus.Unavailable, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AzureAnalyzer_InvalidEndpoint_ReturnsUnavailableWithoutHttpCall()
    {
        var handler = new StubHandler(
            _ => throw new InvalidOperationException("HTTP must not be called."));
        var analyzer = new AzureOpenAiVisionAnalyzer(
            new BaristaVisionConfiguration("not-a-url", "test-key"),
            new HttpClient(handler));

        var result = await analyzer.ClassifyPhotoAsync(CameraPhoto);

        Assert.Equal(VisionAvailabilityState.NotConfigured, analyzer.Availability.State);
        Assert.Contains("absolute HTTP", analyzer.Availability.Reason);
        Assert.Equal(VisionRequestStatus.Unavailable, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AzureAnalyzer_ClassificationJson_MapsCoffeeDetails()
    {
        var responseJson = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"intent\":\"coffee\",\"isObvious\":true,\"rationale\":\"Coffee label\",\"name\":\"Monarch\",\"roaster\":\"Onyx\",\"origin\":\"Ethiopia\",\"roastDate\":\"2026-09-01\",\"notes\":\"washed\"}"
                  }
                }
              ]
            }
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
        var analyzer = new AzureOpenAiVisionAnalyzer(
            new BaristaVisionConfiguration("https://example.openai.azure.com", "test-key"),
            new HttpClient(handler));

        var result = await analyzer.ClassifyPhotoAsync(CameraPhoto);

        Assert.Equal(VisionRequestStatus.Success, result.Status);
        Assert.Equal(PhotoWorkflowIntent.Coffee, result.Analysis.Intent);
        Assert.True(result.Analysis.IsObvious);
        Assert.Equal("Monarch", result.CoffeeDetails?.Name);
        Assert.Equal(new DateTime(2026, 9, 1), result.CoffeeDetails?.RoastDate);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(4000, 3000, 1024, 768)]
    [InlineData(3000, 4000, 768, 1024)]
    [InlineData(640, 480, 640, 480)]
    public void PhotoNormalization_FitWithin_BoundsWithoutUpscaling(
        int width,
        int height,
        int expectedWidth,
        int expectedHeight)
    {
        var actual = BaristaPhotoNormalization.FitWithin(width, height);

        Assert.Equal((expectedWidth, expectedHeight), actual);
        Assert.Equal(1024, BaristaPhotoNormalization.MaximumDimension);
        Assert.Equal(70, BaristaPhotoNormalization.JpegQuality);
    }

    [Fact]
    public async Task AzureAnalyzer_OversizedPhoto_RejectsBeforeHttpUpload()
    {
        var handler = new StubHandler(
            _ => throw new InvalidOperationException("HTTP must not be called."));
        var analyzer = new AzureOpenAiVisionAnalyzer(
            new BaristaVisionConfiguration("https://example.openai.azure.com", "test-key"),
            new HttpClient(handler));
        var oversized = CameraPhoto with
        {
            Bytes = new byte[BaristaPhotoNormalization.MaximumUploadBytes + 1],
        };

        var result = await analyzer.ClassifyPhotoAsync(oversized);

        Assert.Equal(VisionRequestStatus.Error, result.Status);
        Assert.Contains("12 MB", result.ErrorMessage);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void PhotoNormalization_CreateResult_UsesPrivateJpegIdentity()
    {
        var result = BaristaPhotoNormalization.CreateResult(
            [0xff, 0xd8, 0xff, 0xd9],
            PhotoSource.Gallery);

        Assert.Equal(PhotoOperationStatus.Success, result.Status);
        Assert.Equal("image/jpeg", result.Photo?.ContentType);
        Assert.StartsWith("baristanotes-", result.Photo?.FileName);
        Assert.EndsWith(".jpg", result.Photo?.FileName);
    }

    [Fact]
    public void PhotoPresentationGate_CancelWins_DoesNotAllowPresentation()
    {
        var gate = new BaristaPhotoNormalization.PhotoPresentationGate();

        Assert.True(gate.TryCancelBeforePresentation());
        Assert.False(gate.TryBeginPresentation());
    }

    [Fact]
    public void PhotoPresentationGate_PresentationWins_CancelIsNoLongerPrePresentation()
    {
        var gate = new BaristaPhotoNormalization.PhotoPresentationGate();

        Assert.True(gate.TryBeginPresentation());
        Assert.False(gate.TryCancelBeforePresentation());
    }

    [Fact]
    public async Task ProfilePhotoMedia_PickerHook_ForwardsCancellationAndInMemoryPhoto()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = PhotoOperationResult.Success(
            CameraPhoto with { Source = PhotoSource.Gallery });
        CancellationToken receivedToken = default;
        ProfilePhotoMedia.PickFromLibrary = token =>
        {
            receivedToken = token;
            return Task.FromResult(expected);
        };

        try
        {
            var actual = await ProfilePhotoMedia.PickAsync(cancellation.Token);

            Assert.Same(expected, actual);
            Assert.Same(expected.Photo?.Bytes, actual.Photo?.Bytes);
            Assert.Equal(cancellation.Token, receivedToken);
        }
        finally
        {
            ProfilePhotoMedia.PickFromLibrary = null;
        }
    }

    static PhotoClassificationResult Classification(
        PhotoWorkflowIntent intent,
        bool isObvious) =>
        new(
            VisionRequestStatus.Success,
            new PhotoWorkflowAnalysis(true, intent, isObvious, null, null),
            null,
            null);

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory, "global.json"))
                && System.IO.Directory.Exists(System.IO.Path.Combine(directory, "sample")))
            {
                return directory;
            }
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        return null;
    }

    sealed class FakePhotoService : IBaristaPhotoService
    {
        public Queue<PhotoOperationResult> CaptureResults { get; init; } = new();
        public Queue<PhotoOperationResult> PickResults { get; init; } = new();
        public int CaptureCount { get; private set; }
        public bool IsCameraAvailable => true;
        public bool IsGalleryAvailable => true;

        public Task<PhotoOperationResult> CapturePhotoAsync(
            CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            return Task.FromResult(CaptureResults.Dequeue());
        }

        public Task<PhotoOperationResult> PickPhotoAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PickResults.Dequeue());
    }

    sealed class FakeVisionAnalyzer : IBaristaVisionAnalyzer
    {
        public VisionAvailability Availability { get; } =
            new(VisionAvailabilityState.Ready, null);
        public Queue<PhotoClassificationResult> Classifications { get; init; } =
            new([Classification(PhotoWorkflowIntent.Unknown, false)]);
        public RoomAnalysisResult RoomResult { get; init; } =
            new(
                VisionRequestStatus.Success,
                new VisionAnalysisResult(true, 1, 1, 18, "One cup.", null),
                null);

        public Task<PhotoClassificationResult> ClassifyPhotoAsync(
            PhotoAsset photo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Classifications.Dequeue());

        public Task<BeanLabelResult> ExtractBeanLabelAsync(
            PhotoAsset photo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BeanLabelResult(
                VisionRequestStatus.Success,
                new BeanLabelExtraction { Success = true },
                null));

        public Task<RoomAnalysisResult> AnalyzeRoomAsync(
            PhotoAsset photo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RoomResult);

        public Task<ProfileMatchResult> MatchProfileAsync(
            PhotoAsset photo,
            IReadOnlyList<PersonIdentificationCandidate> candidates,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileMatchResult(
                VisionRequestStatus.Success,
                new PersonIdentificationResult(true, null, null, null, null),
                null));
    }

    sealed class RecordingCallbacks : IBaristaPhotoWorkflowCallbacks
    {
        public Queue<PhotoIntentChoice> Choices { get; init; } = new();
        public List<string> Routes { get; } = [];
        public List<VisionCallback> VisionEvents { get; } = [];
        public bool IsBusy { get; private set; }
        public PhotoWorkflowCancellation? Cancellation { get; private set; }
        public PhotoWorkflowError? Error { get; private set; }

        public void SetPhotoWorkflowBusy(bool isBusy) => IsBusy = isBusy;

        public Task VisionResultAsync(
            VisionCallback result,
            CancellationToken cancellationToken)
        {
            VisionEvents.Add(result);
            return Task.CompletedTask;
        }

        public Task<PhotoIntentChoice> ChoosePhotoIntentAsync(
            PhotoAsset photo,
            PhotoClassificationResult classification,
            CancellationToken cancellationToken) =>
            Task.FromResult(Choices.Dequeue());

        public Task RouteCoffeeAsync(
            PhotoAsset photo,
            BeanLabelExtraction? coffeeDetails,
            CancellationToken cancellationToken)
        {
            Routes.Add("coffee");
            return Task.CompletedTask;
        }

        public Task RouteProfileAsync(
            PhotoAsset photo,
            CancellationToken cancellationToken)
        {
            Routes.Add("profile");
            return Task.CompletedTask;
        }

        public Task RouteRoomAsync(
            PhotoAsset photo,
            VisionAnalysisResult analysis,
            CancellationToken cancellationToken)
        {
            Routes.Add("room");
            return Task.CompletedTask;
        }

        public Task PhotoWorkflowCancelledAsync(
            PhotoWorkflowCancellation reason,
            CancellationToken cancellationToken)
        {
            Cancellation = reason;
            return Task.CompletedTask;
        }

        public Task PhotoWorkflowFailedAsync(
            PhotoWorkflowError error,
            CancellationToken cancellationToken)
        {
            Error = error;
            return Task.CompletedTask;
        }
    }

    sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(respond(request));
        }
    }
}
