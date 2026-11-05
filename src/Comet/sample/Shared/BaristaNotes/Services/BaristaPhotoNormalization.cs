#nullable enable
using System;
using System.Threading;

namespace CometBaristaNotes.Services;

public static class BaristaPhotoNormalization
{
    public const int MaximumDimension = 1024;
    public const int JpegQuality = 70;
    public const int MaximumSourceBytes = 12 * 1024 * 1024;
    public const int MaximumUploadBytes = 12 * 1024 * 1024;

    public static (int Width, int Height) FitWithin(int width, int height)
    {
        if (width < 1)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1)
            throw new ArgumentOutOfRangeException(nameof(height));

        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumDimension / width,
                (double)MaximumDimension / height));
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    public sealed class PhotoPresentationGate
    {
        int _state;

        public bool TryBeginPresentation() =>
            Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        public bool TryCancelBeforePresentation() =>
            Interlocked.CompareExchange(ref _state, 2, 0) == 0;
    }

    public static PhotoOperationResult CreateResult(
        byte[] jpegBytes,
        PhotoSource source)
    {
        ArgumentNullException.ThrowIfNull(jpegBytes);
        if (jpegBytes.Length < 4
            || jpegBytes[0] != 0xff
            || jpegBytes[1] != 0xd8
            || jpegBytes[^2] != 0xff
            || jpegBytes[^1] != 0xd9)
        {
            return PhotoOperationResult.Error(
                "photo_normalization_failed",
                "The selected photo could not be converted to JPEG.");
        }
        if (jpegBytes.Length > MaximumUploadBytes)
        {
            return PhotoOperationResult.Error(
                "photo_too_large",
                "The normalized photo exceeds the 12 MB upload limit.");
        }

        return PhotoOperationResult.Success(
            new PhotoAsset(
                jpegBytes,
                "image/jpeg",
                $"baristanotes-{Guid.NewGuid():N}.jpg",
                source));
    }
}
