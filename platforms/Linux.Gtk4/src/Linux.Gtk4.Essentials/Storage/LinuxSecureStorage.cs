using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage;

/// <summary>
/// Stores secrets in the desktop Secret Service through libsecret's
/// <c>secret-tool</c> client. Values are sent through standard input and are
/// never placed in command-line arguments or persisted by this implementation.
/// </summary>
public sealed class LinuxSecureStorage : ISecureStorage, IDisposable
{
	readonly ISecretServiceBackend _backend;

	public LinuxSecureStorage()
		: this(new SecretToolBackend(
			"secret-tool",
			AppDomain.CurrentDomain.FriendlyName))
	{
	}

	internal LinuxSecureStorage(ISecretServiceBackend backend)
	{
		_backend = backend;
	}

	public string Backend => "Secret Service (libsecret secret-tool)";

	public Task<string?> GetAsync(string key) => _backend.GetAsync(key);

	public Task SetAsync(string key, string value) => _backend.SetAsync(key, value);

	public bool Remove(string key) => _backend.RemoveAsync(key).GetAwaiter().GetResult();

	public void RemoveAll() => _backend.RemoveAllAsync().GetAwaiter().GetResult();

	public void Dispose()
	{
	}
}

internal interface ISecretServiceBackend
{
	Task<string?> GetAsync(string key);
	Task SetAsync(string key, string value);
	Task<bool> RemoveAsync(string key);
	Task RemoveAllAsync();
}

internal sealed class SecretToolBackend : ISecretServiceBackend
{
	const string ApplicationAttribute = "application";
	const string KeyAttribute = "key";
	internal const int MaxSecretUtf8Bytes = 8191;
	readonly string _executable;
	readonly string _applicationId;
	readonly ISecretToolProcessRunner _processRunner;

	public SecretToolBackend(string executable, string applicationId)
		: this(executable, applicationId, new SecretToolProcessRunner())
	{
	}

	internal SecretToolBackend(
		string executable,
		string applicationId,
		ISecretToolProcessRunner processRunner)
	{
		_executable = executable;
		_applicationId = applicationId;
		_processRunner = processRunner;
	}

	public async Task<string?> GetAsync(string key)
	{
		ValidateKey(key);
		var result = await RunAsync(
			["lookup", ApplicationAttribute, _applicationId, KeyAttribute, EncodeKey(key)])
			.ConfigureAwait(false);
		return result.ExitCode switch
		{
			0 => result.StandardOutput,
			1 when result.StandardError.Length == 0 => null,
			_ => throw CreateServiceException("read", result),
		};
	}

	public async Task SetAsync(string key, string value)
	{
		ValidateKey(key);
		ArgumentNullException.ThrowIfNull(value);
		ValidateSecretSize(value);

		var result = await RunAsync(
			["store", $"--label=.NET MAUI secure storage ({_applicationId})",
				ApplicationAttribute, _applicationId, KeyAttribute, EncodeKey(key)],
			value).ConfigureAwait(false);
		if (result.ExitCode != 0)
			throw CreateServiceException("write", result);
	}

	public async Task<bool> RemoveAsync(string key)
	{
		ValidateKey(key);
		var result = await RunAsync(
			["clear", ApplicationAttribute, _applicationId, KeyAttribute, EncodeKey(key)])
			.ConfigureAwait(false);
		return result.ExitCode switch
		{
			0 => true,
			1 when result.StandardError.Length == 0 => false,
			_ => throw CreateServiceException("remove", result),
		};
	}

	public async Task RemoveAllAsync()
	{
		var result = await RunAsync(["clear", ApplicationAttribute, _applicationId])
			.ConfigureAwait(false);
		if (result.ExitCode != 0 &&
			(result.ExitCode != 1 || result.StandardError.Length != 0))
		{
			throw CreateServiceException("clear", result);
		}
	}

	Task<SecretToolProcessResult> RunAsync(
		IReadOnlyList<string> arguments,
		string? standardInput = null) =>
		_processRunner.RunAsync(_executable, arguments, standardInput);

	static InvalidOperationException CreateServiceException(
		string operation,
		SecretToolProcessResult result)
	{
		var detail = string.IsNullOrWhiteSpace(result.StandardError)
			? $"secret-tool exited with code {result.ExitCode}"
			: result.StandardError.Trim();
		return new InvalidOperationException(
			$"Unable to {operation} secure storage through the Secret Service: {detail}.");
	}

	static void ValidateKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key))
			throw new ArgumentException("Secure storage keys must not be empty.", nameof(key));
	}

	static void ValidateSecretSize(string value)
	{
		var byteCount = Encoding.UTF8.GetByteCount(value);
		if (byteCount > MaxSecretUtf8Bytes)
		{
			throw new ArgumentException(
				$"Secure storage values must be at most {MaxSecretUtf8Bytes} UTF-8 bytes because secret-tool truncates larger standard input.",
				nameof(value));
		}
	}

	static string EncodeKey(string key) =>
		Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
}

internal interface ISecretToolProcessRunner
{
	Task<SecretToolProcessResult> RunAsync(
		string executable,
		IReadOnlyList<string> arguments,
		string? standardInput);
}

internal sealed class SecretToolProcessRunner : ISecretToolProcessRunner
{
	public async Task<SecretToolProcessResult> RunAsync(
		string executable,
		IReadOnlyList<string> arguments,
		string? standardInput)
	{
		var startInfo = new ProcessStartInfo(executable)
		{
			UseShellExecute = false,
			RedirectStandardInput = standardInput is not null,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		foreach (var argument in arguments)
			startInfo.ArgumentList.Add(argument);

		Process process;
		try
		{
			process = Process.Start(startInfo)
				?? throw new FeatureNotSupportedException("The Secret Service client could not be started.");
		}
		catch (Win32Exception ex)
		{
			throw new FeatureNotSupportedException(
				"Secure storage requires libsecret's 'secret-tool' command and a freedesktop.org Secret Service provider.",
				ex);
		}

		using (process)
		{
			if (standardInput is not null)
			{
				await process.StandardInput.WriteAsync(standardInput).ConfigureAwait(false);
				await process.StandardInput.FlushAsync().ConfigureAwait(false);
				process.StandardInput.Close();
			}

			var standardOutputTask = process.StandardOutput.ReadToEndAsync();
			var standardErrorTask = process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync().ConfigureAwait(false);
			return new SecretToolProcessResult(
				process.ExitCode,
				await standardOutputTask.ConfigureAwait(false),
				await standardErrorTask.ConfigureAwait(false));
		}
	}
}

internal readonly record struct SecretToolProcessResult(
	int ExitCode,
	string StandardOutput,
	string StandardError);
