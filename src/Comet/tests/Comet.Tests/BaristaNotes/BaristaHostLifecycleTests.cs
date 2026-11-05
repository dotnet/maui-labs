#if BARISTA_LIFECYCLE_TESTS
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Voice;
using CometSamples.BaristaNotes;
using CometSamples.BaristaNotes.Styles;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaLifecycle;

[CollectionDefinition("Barista host lifecycle", DisableParallelization = true)]
public sealed class BaristaHostLifecycleCollection;

[Collection("Barista host lifecycle")]
public sealed class BaristaHostLifecycleTests
{
    [Fact]
    public async Task ActualAppRoot_DestroyAndRecreate_WaitsForVoiceAndPreservesSelectedStore()
    {
        Comet.ThreadHelper.SetFireOnMainThread(action => action());
        var directory = IOPath.Combine(AppContext.BaseDirectory, "artifacts",
            "barista-lifecycle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = IOPath.Combine(directory, "selected.db");
        BaristaAppStorage.Current.ConfigureComparisonDatabase(database);
        var ownership = new BaristaHostOwnership();
        var events = new List<string>();
        var firstActivity = new object();
        var secondActivity = new object();
        var speech = new DelayedSpeech();
        await BaristaVoiceIntegration.ConfigurePlatformAsync(speech);
        var firstHost = new BaristaHostRoot<BaristaNotesApp>(
            () => events.Add("first-native-detached"),
            async () =>
            {
                events.Add("first-platform-shutdown");
                await BaristaVoiceIntegration.ShutdownAsync();
                events.Add("first-platform-closed");
            });
        var secondHost = new BaristaHostRoot<BaristaNotesApp>(
            () => events.Add("second-native-detached"),
            () => BaristaVoiceIntegration.ShutdownAsync());
        Task<BaristaNotesApp>? replacement = null;
        await ownership.AcquireAsync(firstActivity, () => firstHost.DisposeAsync().AsTask());
        try
        {
            var firstRoot = firstHost.Create(() => new BaristaNotesApp());
            var firstServices = firstRoot.Services;
            Assert.NotNull(BaristaVoiceIntegration.Session);
            Assert.Empty(firstServices.Store.Shots);
            var bean = await firstServices.BeanService.CreateBeanAsync(new CreateBeanDto { Name = "Same-process data" });
            Assert.True(bean.Success);
            firstServices.ThemePreferences.Save(CoffeeThemeMode.Dark);
            firstServices.Preferences.SetLastDrinkType("Latte");
            CoffeeTheme.SetMode(CoffeeThemeMode.Dark);

            // This is the production host's teardown, not a test-side services.Dispose().
            var destroying = ownership.ReleaseAsync(firstActivity);
            await speech.CancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            replacement = CreateReplacementAsync();
            Assert.False(destroying.IsCompleted);
            Assert.False(replacement.IsCompleted);
            Assert.Null(secondHost.Root);
            Assert.Equal(new[] { "first-native-detached" }, events);
            Assert.Throws<InvalidOperationException>(() => new BaristaServices());
            Assert.Single(await firstServices.BeanService.GetAllActiveBeansAsync());

            speech.AllowCancel.SetResult(true);
            await speech.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(replacement.IsCompleted);
            Assert.Null(secondHost.Root);
            var closedStore = Assert.Throws<InvalidOperationException>(
                () => firstServices.Preferences.GetLastDrinkType());
            Assert.StartsWith("SQLite error 21:", closedStore.Message);
            speech.AllowDispose.SetResult(true);
            var secondRoot = await replacement.WaitAsync(TimeSpan.FromSeconds(10));
            await destroying;
            Assert.Equal(new[] { "first-native-detached", "first-platform-shutdown",
                "first-platform-closed", "second-root-created" }, events);
            Assert.NotSame(firstServices, secondRoot.Services);
            Assert.NotSame(firstServices.Store, secondRoot.Services.Store);
            Assert.Equal(database, Assert.IsType<SqliteDataStore>(secondRoot.Services.Store).DatabasePath);
            Assert.Equal("Same-process data",
                Assert.Single(await secondRoot.Services.BeanService.GetAllActiveBeansAsync()).Name);
            Assert.Equal(CoffeeThemeMode.Dark, secondRoot.Services.ThemePreferences.Load());
            Assert.Equal("Latte", secondRoot.Services.Preferences.GetLastDrinkType());
            Assert.Equal(CoffeeThemeMode.Dark, CoffeeTheme.Mode.Value);
            Assert.Empty(secondRoot.Services.Store.Shots);
            Assert.False(secondRoot.HasActiveActivityFilters);
            Assert.Throws<ObjectDisposedException>(() => firstHost.Create(() => new BaristaNotesApp()));
            await ownership.ReleaseAsync(firstActivity);
            Assert.True(ownership.IsOwner(secondActivity));
            Assert.Single(await secondRoot.Services.BeanService.GetAllActiveBeansAsync());
        }
        finally
        {
            speech.AllowCancel.TrySetResult(true);
            speech.AllowDispose.TrySetResult(true);
            try
            {
                if (replacement is not null)
                    await replacement.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await ownership.ReleaseAsync(firstActivity);
                await ownership.ReleaseAsync(secondActivity);
            }
        }

        async Task<BaristaNotesApp> CreateReplacementAsync()
        {
            await ownership.AcquireAsync(secondActivity, () => secondHost.DisposeAsync().AsTask());
            await BaristaVoiceIntegration.ConfigurePlatformAsync(new ImmediateSpeech());
            var root = secondHost.Create(() => new BaristaNotesApp());
            events.Add("second-root-created");
            return root;
        }
    }

    class ImmediateSpeech : IPlatformSpeechRecognizer
    {
        public event EventHandler<string>? PartialResultReceived { add { } remove { } }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<SpeechPermissionStatus> GetPermissionStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechPermissionStatus.Granted);
        public Task<SpeechPermissionStatus> RequestPermissionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechPermissionStatus.Granted);
        public Task<PlatformSpeechRecognitionResult> StartListeningAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The lifecycle test must not start recognition.");
        public Task StopListeningAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public virtual Task CancelListeningAsync() => Task.CompletedTask;
        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class DelayedSpeech : ImmediateSpeech
    {
        public TaskCompletionSource<bool> CancelEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AllowCancel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> AllowDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task CancelListeningAsync()
        {
            CancelEntered.TrySetResult(true);
            return AllowCancel.Task;
        }

        public override ValueTask DisposeAsync()
        {
            DisposeEntered.TrySetResult(true);
            return new ValueTask(AllowDispose.Task);
        }
    }
}
#endif
