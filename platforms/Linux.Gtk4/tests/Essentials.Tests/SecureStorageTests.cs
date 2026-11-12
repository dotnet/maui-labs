using System.Collections.Concurrent;
using System.Text;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage;

namespace Essentials.Tests;

public class SecureStorageTests
{
	[Fact]
	public async Task Operations_DelegateToSecureBackend()
	{
		var backend = new RecordingBackend();
		var storage = new LinuxSecureStorage(backend);

		await storage.SetAsync("token", "secret");
		Assert.Equal("secret", await storage.GetAsync("token"));
		Assert.True(storage.Remove("token"));
		Assert.False(storage.Remove("token"));

		await storage.SetAsync("one", "1");
		await storage.SetAsync("two", "2");
		storage.RemoveAll();

		Assert.Empty(backend.Values);
	}

	[Theory]
	[InlineData("secret\n")]
	[InlineData("secret\r\n")]
	[InlineData("secret\n\n")]
	[InlineData("")]
	public async Task Lookup_PreservesExactRedirectedOutput(string output)
	{
		var backend = CreateBackend(new SecretToolProcessResult(0, output, ""));

		Assert.Equal(output, await backend.GetAsync("token"));
	}

	[Fact]
	public async Task Lookup_ExitOneDistinguishesAbsenceFromServiceFailure()
	{
		var missing = CreateBackend(new SecretToolProcessResult(1, "", ""));
		Assert.Null(await missing.GetAsync("token"));

		var failed = CreateBackend(new SecretToolProcessResult(
			1, "", "The name org.freedesktop.secrets was not provided"));
		var error = await Assert.ThrowsAsync<InvalidOperationException>(
			() => failed.GetAsync("token"));
		Assert.Contains("org.freedesktop.secrets", error.Message);
	}

	[Fact]
	public async Task Clear_ExitOneDistinguishesAbsenceFromServiceFailure()
	{
		var noMatch = CreateBackend(
			new SecretToolProcessResult(1, "", ""),
			new SecretToolProcessResult(1, "", ""));
		Assert.False(await noMatch.RemoveAsync("token"));
		await noMatch.RemoveAllAsync();

		var removeFailure = CreateBackend(new SecretToolProcessResult(1, "", "keyring locked"));
		var removeError = await Assert.ThrowsAsync<InvalidOperationException>(
			() => removeFailure.RemoveAsync("token"));
		Assert.Contains("keyring locked", removeError.Message);

		var clearFailure = CreateBackend(new SecretToolProcessResult(1, "", "D-Bus unavailable"));
		var clearError = await Assert.ThrowsAsync<InvalidOperationException>(
			() => clearFailure.RemoveAllAsync());
		Assert.Contains("D-Bus unavailable", clearError.Message);
	}

	[Fact]
	public async Task Set_EnforcesSecretToolUtf8BoundaryWithoutIncludingSecret()
	{
		var runner = new RecordingProcessRunner(
			new SecretToolProcessResult(0, "", ""));
		var backend = new SecretToolBackend("secret-tool", "tests", runner);
		var accepted = new string('\u00e9', 4095) + "a";
		var rejected = new string('\u00e9', 4096);

		Assert.Equal(SecretToolBackend.MaxSecretUtf8Bytes, Encoding.UTF8.GetByteCount(accepted));
		Assert.Equal(SecretToolBackend.MaxSecretUtf8Bytes + 1, Encoding.UTF8.GetByteCount(rejected));
		await backend.SetAsync("accepted", accepted);
		Assert.Equal(accepted, runner.StandardInputs.Single());

		var error = await Assert.ThrowsAsync<ArgumentException>(
			() => backend.SetAsync("rejected", rejected));
		Assert.Contains(SecretToolBackend.MaxSecretUtf8Bytes.ToString(), error.Message);
		Assert.DoesNotContain(rejected[..16], error.Message);
		Assert.Single(runner.StandardInputs);
	}

	[Fact]
	public void SynchronousRemoveOperations_DoNotCaptureSingleThreadContext()
	{
		var runner = new RecordingProcessRunner(
			new SecretToolProcessResult(0, "", ""),
			new SecretToolProcessResult(1, "", ""));
		var storage = new LinuxSecureStorage(
			new SecretToolBackend("secret-tool", "tests", runner));
		using var context = new NonPumpingSynchronizationContext();
		using var completed = new ManualResetEventSlim();
		Exception? failure = null;
		var removed = false;
		var thread = new Thread(() =>
		{
			SynchronizationContext.SetSynchronizationContext(context);
			try
			{
				removed = storage.Remove("token");
				storage.RemoveAll();
			}
			catch (Exception ex)
			{
				failure = ex;
			}
			finally
			{
				completed.Set();
			}
		})
		{
			IsBackground = true,
		};

		thread.Start();
		var finishedWithoutPumping = completed.Wait(TimeSpan.FromSeconds(2));
		context.Release();
		Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "The storage thread did not terminate.");
		Assert.True(finishedWithoutPumping, "Synchronous secure-storage removal captured the caller context.");
		Assert.Null(failure);
		Assert.True(removed);
	}

	[Fact]
	public async Task MissingSecretTool_IsExplicitlyUnsupported()
	{
		var backend = new SecretToolBackend(
			$"missing-secret-tool-{Guid.NewGuid():N}",
			"secure-storage-tests");

		await Assert.ThrowsAsync<FeatureNotSupportedException>(() => backend.GetAsync("token"));
	}

	static SecretToolBackend CreateBackend(params SecretToolProcessResult[] results) =>
		new("secret-tool", "tests", new RecordingProcessRunner(results));

	sealed class RecordingProcessRunner(params SecretToolProcessResult[] results)
		: ISecretToolProcessRunner
	{
		readonly ConcurrentQueue<SecretToolProcessResult> _results = new(results);

		public List<string> StandardInputs { get; } = [];

		public Task<SecretToolProcessResult> RunAsync(
			string executable,
			IReadOnlyList<string> arguments,
			string? standardInput)
		{
			if (standardInput is not null)
				StandardInputs.Add(standardInput);
			if (!_results.TryDequeue(out var result))
				throw new InvalidOperationException("No process result was configured.");
			return Task.Run(() => result);
		}
	}

	sealed class NonPumpingSynchronizationContext : SynchronizationContext, IDisposable
	{
		readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = [];

		public override void Post(SendOrPostCallback d, object? state) =>
			_callbacks.Enqueue((d, state));

		public void Release()
		{
			while (_callbacks.TryDequeue(out var item))
				ThreadPool.QueueUserWorkItem(_ => item.Callback(item.State));
		}

		public void Dispose() => Release();
	}

	sealed class RecordingBackend : ISecretServiceBackend
	{
		public Dictionary<string, string> Values { get; } = [];

		public Task<string?> GetAsync(string key) =>
			Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

		public Task SetAsync(string key, string value)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}

		public Task<bool> RemoveAsync(string key) =>
			Task.FromResult(Values.Remove(key));

		public Task RemoveAllAsync()
		{
			Values.Clear();
			return Task.CompletedTask;
		}
	}
}
