using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using Xunit;
using RealDataChangeNotifier = CometBaristaNotes.Services.IDataChangeNotifier;
using RealDataChangedEventArgs = CometBaristaNotes.Services.DataChangedEventArgs;
using RealDataChangeType = CometBaristaNotes.Models.Enums.DataChangeType;

namespace Comet.Tests.BaristaNotes.Domain;

public sealed class BaristaNotesImageProcessingTests : IDisposable
{
    private const int MaximumImageBytes = 12 * 1024 * 1024;
    private readonly string _root = System.IO.Path.Combine(
        Environment.CurrentDirectory,
        "artifacts",
        "barista-image-processing-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidateImageAsync_DecodablePng_ReturnsValid()
    {
        using var stream = new MemoryStream(CreatePng(200, 100));
        var result = await Service().ValidateImageAsync(stream);
        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateImageAsync_ArbitraryBytes_ReturnsInvalid()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5]);
        var result = await Service().ValidateImageAsync(stream);
        Assert.False(result.IsValid);
        Assert.Contains("cannot be decoded", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateImageAsync_OverSizeLimit_ReturnsInvalidBeforeDecode()
    {
        var codec = new TestImageCodec();
        using var stream = new MemoryStream(new byte[MaximumImageBytes + 1]);
        var result = await Service(codec).ValidateImageAsync(stream);
        Assert.False(result.IsValid);
        Assert.Equal("Image is too large", result.ErrorMessage);
        Assert.Equal(0, codec.DecodeAttempts);
    }

    [Fact]
    public async Task DownsampleAsync_LargePng_BoundsDimensionsAndReencodesJpeg()
    {
        var codec = new TestImageCodec();
        using var source = new MemoryStream(CreatePng(800, 600));
        using var result = await Service(codec).DownsampleAsync(source, maxDimension: 400, quality: 85);

        Assert.NotNull(result);
        Assert.Equal(0xFF, result!.ReadByte());
        Assert.Equal(0xD8, result.ReadByte());
        Assert.Equal(400, codec.OutputWidth);
        Assert.Equal(300, codec.OutputHeight);
        Assert.Equal(85, codec.Quality);
    }

    [Fact]
    public async Task UpdateProfileImageAsync_UndecodableBytes_DoesNotChangeProfileState()
    {
        var store = new InMemoryDataStore();
        var images = Service();
        var profiles = new InMemoryUserProfileService(store, new DataChangeNotifier(), images);
        var profile = await profiles.CreateProfileAsync(new CreateUserProfileDto { Name = "No mutation" });

        var result = await profiles.UpdateProfileImageAsync(profile.Id, new MemoryStream([1, 2, 3]));

        Assert.False(result.Success);
        Assert.Null(store.Profiles.Single().AvatarPath);
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task SaveImageAsync_NonJpegBytes_RejectsWithoutCreatingFile()
    {
        var service = Service();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SaveImageAsync(new MemoryStream(CreatePng(100, 100)), "profile_avatar_1_deadbeef.jpg"));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task ValidateImageAsync_NonSeekableOversize_StopsAtBoundWithoutDecode()
    {
        var codec = new TestImageCodec();
        using var stream = new CountingInfiniteStream();

        var result = await Service(codec).ValidateImageAsync(stream);

        Assert.False(result.IsValid);
        Assert.Equal("Image is too large", result.ErrorMessage);
        Assert.InRange(stream.BytesRead, MaximumImageBytes + 1L, MaximumImageBytes + 1L);
        Assert.Equal(0, codec.DecodeAttempts);
    }

    [Fact]
    public async Task UpdateProfileImageAsync_SaveFailure_RestoresOldStateAndDeletesOnlyNewFile()
    {
        var store = new FailingDataStore();
        var images = new RecordingImageProcessingService();
        var profiles = new InMemoryUserProfileService(store, new DataChangeNotifier(), images);
        var profile = await profiles.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Rollback",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });
        store.FailSaves = true;

        var result = await profiles.UpdateProfileImageAsync(profile.Id, new MemoryStream([1, 2, 3]));

        Assert.False(result.Success);
        Assert.Equal("profile_avatar_1_deadbeef.jpg", store.Profiles.Single().AvatarPath);
        var newFilename = Assert.Single(images.Saved);
        Assert.Contains(newFilename, images.Deleted);
        Assert.DoesNotContain("profile_avatar_1_deadbeef.jpg", images.Deleted);
    }

    [Fact]
    public async Task UpdateProfileImageAsync_NotificationFailure_KeepsCommittedNewFile()
    {
        var store = new InMemoryDataStore();
        var images = new RecordingImageProcessingService();
        var creator = new InMemoryUserProfileService(store, new DataChangeNotifier(), images);
        var profile = await creator.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Committed",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });
        var profiles = new InMemoryUserProfileService(store, new ThrowingDataChangeNotifier(), images);

        var result = await profiles.UpdateProfileImageAsync(profile.Id, new MemoryStream([1, 2, 3]));

        Assert.True(result.Success);
        Assert.Equal(result.NewAvatarPath, store.Profiles.Single().AvatarPath);
        Assert.Contains("profile_avatar_1_deadbeef.jpg", images.Deleted);
        Assert.DoesNotContain(result.NewAvatarPath!, images.Deleted);
    }

    [Fact]
    public async Task ProfileSave_NotificationFailure_ReturnsCommittedCreateAndUpdate()
    {
        var store = new InMemoryDataStore();
        var images = new RecordingImageProcessingService();
        var profiles = new InMemoryUserProfileService(
            store,
            new ThrowingDataChangeNotifier(),
            images);

        var created = await profiles.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Committed",
        });
        var updated = await profiles.UpdateProfileAsync(created.Id, new UpdateUserProfileDto
        {
            Name = "Still committed",
        });

        Assert.Equal("Still committed", updated.Name);
        Assert.Equal("Still committed", store.Profiles.Single().Name);
    }

    [Fact]
    public async Task RemoveAndDelete_SaveFailure_RestoreStateAndRetainOwnedFile()
    {
        var store = new FailingDataStore();
        var images = new RecordingImageProcessingService();
        var profiles = new InMemoryUserProfileService(store, new DataChangeNotifier(), images);
        var profile = await profiles.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Rollback",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });
        store.FailSaves = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.RemoveProfileImageAsync(profile.Id));
        Assert.Equal("profile_avatar_1_deadbeef.jpg", store.Profiles.Single().AvatarPath);
        Assert.Empty(images.Deleted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => profiles.DeleteProfileAsync(profile.Id));
        Assert.False(store.Profiles.Single().IsDeleted);
        Assert.Empty(images.Deleted);
    }

    [Fact]
    public async Task AvatarDeletionFailure_IsQueuedAndRetriedByLaterProfileOperation()
    {
        var store = new InMemoryDataStore();
        var images = new FlakyDeleteImageProcessingService { FailuresRemaining = 2 };
        var profiles = new InMemoryUserProfileService(store, new DataChangeNotifier(), images);
        var profile = await profiles.CreateProfileAsync(new CreateUserProfileDto
        {
            Name = "Retry cleanup",
            AvatarPath = "profile_avatar_1_deadbeef.jpg"
        });

        var result = await profiles.UpdateProfileImageAsync(profile.Id, new MemoryStream([1, 2, 3]));

        Assert.True(result.Success);
        var pending = Assert.Single(store.PendingAvatarCleanups);
        Assert.Equal("profile_avatar_1_deadbeef.jpg", pending.AvatarPath);
        Assert.Equal(1, pending.Attempts);

        await profiles.GetProfileByIdAsync(profile.Id);
        pending = Assert.Single(store.PendingAvatarCleanups);
        Assert.Equal(2, pending.Attempts);
        Assert.NotNull(pending.LastError);

        images.FailuresRemaining = 0;
        await profiles.GetProfileByIdAsync(profile.Id);
        Assert.Empty(store.PendingAvatarCleanups);
        Assert.Contains("profile_avatar_1_deadbeef.jpg", images.Deleted);
    }

    [Fact]
    public async Task ServiceStartup_RetriesDurablePendingAvatarCleanup()
    {
        var store = new InMemoryDataStore();
        store.PendingAvatarCleanups.Add(new CometBaristaNotes.Models.PendingAvatarCleanup
        {
            ProfileId = 7,
            AvatarPath = "profile_avatar_7_deadbeef.jpg",
            Attempts = 3,
            LastAttemptAt = DateTime.UtcNow.AddMinutes(-1),
            LastError = "Previous failure"
        });
        var images = new FlakyDeleteImageProcessingService();
        var profiles = new InMemoryUserProfileService(store, new DataChangeNotifier(), images);

        await profiles.GetAllProfilesAsync();

        Assert.Empty(store.PendingAvatarCleanups);
        Assert.Contains("profile_avatar_7_deadbeef.jpg", images.Deleted);
    }

    [Theory]
    [InlineData(12000, 500, 16)]
    [InlineData(500, 12000, 16)]
    public void CalculateInSampleSize_HighAspectRatio_UsesLongestDimension(
        int width,
        int height,
        int expected)
    {
        Assert.Equal(expected, PlatformImageCodec.CalculateInSampleSize(width, height, 400));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
            return;
        foreach (var file in Directory.GetFiles(_root))
            File.Delete(file);
        Directory.Delete(_root);
    }

    internal sealed class FailingDataStore : InMemoryDataStore
    {
        public bool FailSaves { get; set; }

        public override void SaveChanges()
        {
            if (FailSaves)
                throw new InvalidOperationException("Injected save failure");
            base.SaveChanges();
        }
    }

    internal sealed class ThrowingDataChangeNotifier : RealDataChangeNotifier
    {
        public event EventHandler<RealDataChangedEventArgs>? DataChanged;

        public void NotifyDataChanged(RealDataChangeType changeType, object? entity = null)
        {
            DataChanged?.Invoke(this, new RealDataChangedEventArgs(changeType, entity));
            throw new InvalidOperationException("Injected notification failure");
        }
    }

    internal sealed class CountingInfiniteStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)1, offset, count);
            BytesRead += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Span.Fill(1);
            BytesRead += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private LocalImageProcessingService Service(TestImageCodec? codec = null) =>
        new(_root, codec ?? new TestImageCodec());

    private static byte[] CreatePng(int width, int height)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR"u8, header);

        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
                raw.Write([0x78, 0x50, 0x28]);
        }
        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            raw.CopyTo(zlib);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput, type.Length);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}

internal sealed class FlakyDeleteImageProcessingService : IImageProcessingService
{
    public int FailuresRemaining { get; set; }
    public List<string> Deleted { get; } = [];

    public Task<ImageValidationResult> ValidateImageAsync(Stream imageStream) =>
        Task.FromResult(ImageValidationResult.Valid());

    public Task<string> SaveImageAsync(Stream imageStream, string filename) =>
        Task.FromResult(filename);

    public Task<bool> DeleteImageAsync(string filename)
    {
        if (FailuresRemaining-- > 0)
            throw new IOException("Injected delete failure");
        Deleted.Add(filename);
        return Task.FromResult(true);
    }

    public string GetImagePath(string filename) => filename;
    public bool ImageExists(string filename) => true;
    public Task<MemoryStream?> DownsampleAsync(Stream imageStream, int maxDimension, int quality) =>
        Task.FromResult<MemoryStream?>(new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]));
}

internal sealed class TestImageCodec : IImageCodec
{
    public int DecodeAttempts { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }
    public int Quality { get; private set; }

    public bool CanDecode(ReadOnlyMemory<byte> bytes)
    {
        DecodeAttempts++;
        return TryReadPngDimensions(bytes.Span, out _, out _);
    }

    public Task<MemoryStream?> DownsampleToJpegAsync(ReadOnlyMemory<byte> bytes, int maxDimension, int quality)
    {
        if (!TryReadPngDimensions(bytes.Span, out var width, out var height))
            return Task.FromResult<MemoryStream?>(null);
        var scale = Math.Min(1d, Math.Min((double)maxDimension / width, (double)maxDimension / height));
        OutputWidth = Math.Max(1, (int)Math.Round(width * scale));
        OutputHeight = Math.Max(1, (int)Math.Round(height * scale));
        Quality = quality;
        return Task.FromResult<MemoryStream?>(new MemoryStream([0xFF, 0xD8, 0xFF, 0xD9]));
    }

    private static bool TryReadPngDimensions(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 24
            || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return false;
        width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        return width > 0 && height > 0;
    }
}
