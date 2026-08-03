// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Routes <c>Xamarin.Apple.Tools.MaciOS</c> diagnostics to stderr.
/// </summary>
/// <remarks>
/// The shipped <see cref="ConsoleLogger"/> writes Info, Debug and Warning to <b>stdout</b>
/// (only Error goes to stderr). That pollutes machine-readable output: with <c>--json</c> the
/// CLI would emit lines such as <c>Info: Executing: /usr/bin/xcrun simctl list devices ...</c>
/// ahead of the JSON document, so a plain <c>JSON.parse(stdout)</c> fails and consumers have to
/// scrape the payload back out.
///
/// Diagnostics are never part of a command's result, so they always belong on stderr regardless
/// of the output format. Debug messages are dropped unless <see cref="Verbose"/> is set.
/// </remarks>
public sealed class StandardErrorToolsLogger : ICustomLogger
{
	readonly TextWriter _writer;

	/// <summary>
	/// Verbosity applied to loggers created without an explicit value. Set once from the global
	/// <c>--verbose</c> flag, because providers are resolved from DI and cannot see the parse result.
	/// </summary>
	public static bool DefaultVerbose { get; set; }

	public StandardErrorToolsLogger(TextWriter? writer = null, bool? verbose = null)
	{
		_writer = writer ?? Console.Error;
		Verbose = verbose ?? DefaultVerbose;
	}

	/// <summary>
	/// When false, <see cref="LogDebug"/> messages are suppressed.
	/// </summary>
	public bool Verbose { get; }

	public void LogError(string message, Exception? exception)
	{
		Write("Error", message);

		if (exception is not null)
			_writer.WriteLine(exception);
	}

	public void LogWarning(string message, params object?[] args) => Write("Warning", message, args);

	public void LogInfo(string message, params object?[] args) => Write("Info", message, args);

	public void LogDebug(string message, params object?[] args)
	{
		if (Verbose)
			Write("Debug", message, args);
	}

	void Write(string level, string message, params object?[] args)
	{
		// The upstream logger uses composite formatting; a malformed template must not take
		// the whole command down, so fall back to the raw message when formatting fails.
		string text;
		try
		{
			text = args is { Length: > 0 } ? string.Format(CultureInfo.InvariantCulture, message, args) : message;
		}
		catch (FormatException)
		{
			text = message;
		}

		_writer.WriteLine($"{level}: {text}");
	}
}
