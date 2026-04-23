// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Models;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Output;

/// <summary>
/// Rich console output formatter using Spectre.Console for tables, spinners, and styled text.
/// </summary>
public class SpectreOutputFormatter : IOutputFormatter
{
	static readonly TimeSpan s_statusRefreshInterval = TimeSpan.FromMilliseconds(200);
	readonly IAnsiConsole _console;
	readonly bool _verbose;

	public IAnsiConsole Console => _console;

	public SpectreOutputFormatter(IAnsiConsole? console = null, bool verbose = false)
	{
		_console = console ?? AnsiConsole.Console;
		_verbose = verbose;
	}

	public bool Verbose => _verbose;

	public void Write<T>(T result)
	{
		WriteResult(result);
	}

	public void WriteResult<T>(T result)
	{
		if (result is null)
			return;

		if (result is DoctorReport report)
		{
			WriteDoctorReport(report);
			return;
		}

		if (result is DeviceListResult deviceList)
		{
			WriteDeviceList(deviceList);
			return;
		}

		_console.MarkupLine(Markup.Escape(result.ToString() ?? string.Empty));
	}

	void WriteDoctorReport(DoctorReport report)
	{
		_console.WriteLine();
		_console.Write(new Rule("[cyan]MAUI Doctor[/]").LeftJustified());
		_console.WriteLine();

		var tree = new Tree(string.Empty);
		tree.Style = Style.Plain;

		foreach (var check in report.Checks)
		{
			var (icon, color) = check.Status switch
			{
				CheckStatus.Ok => ("✓", "green"),
				CheckStatus.Warning => ("⚠", "yellow"),
				CheckStatus.Error => ("✗", "red"),
				CheckStatus.Skipped => ("○", "grey"),
				_ => ("?", "grey")
			};

			var label = $"[{color}]{icon}[/] {Markup.Escape(check.Name)}";
			if (!string.IsNullOrEmpty(check.Message))
				label += $" [grey]- {Markup.Escape(check.Message)}[/]";

			var node = tree.AddNode(label);

			if (check.Fix != null && !check.Fix.AutoFixable)
			{
				node.AddNode($"[yellow]Fix:[/] {Markup.Escape(check.Fix.Description)}");
			}
		}

		_console.Write(tree);
		_console.WriteLine();

		var statusColor = report.Status switch
		{
			HealthStatus.Healthy => "green",
			HealthStatus.Degraded => "yellow",
			HealthStatus.Unhealthy => "red",
			_ => "grey"
		};

		_console.MarkupLine($"[{statusColor}]Status: {report.Status}[/] ({report.Summary.Ok} ok, {report.Summary.Warning} warning, {report.Summary.Error} error)");
		_console.WriteLine();
	}

	void WriteDeviceList(DeviceListResult result)
	{
		if (result.Devices.Count == 0)
		{
			WriteWarning("No devices found");
			return;
		}

		var table = new Table()
			.Border(TableBorder.Rounded)
			.AddColumn(new TableColumn("[cyan]Platform[/]"))
			.AddColumn(new TableColumn("[cyan]Type[/]"))
			.AddColumn(new TableColumn("[cyan]State[/]"))
			.AddColumn(new TableColumn("[cyan]ID[/]"))
			.AddColumn(new TableColumn("[cyan]Name[/]"));

		foreach (var device in result.Devices)
		{
			var stateColor = device.State switch
			{
				DeviceState.Booted => "green",
				DeviceState.Connected => "green",
				DeviceState.Booting => "yellow",
				DeviceState.Shutdown => "grey",
				DeviceState.Offline => "red",
				_ => "white"
			};

			table.AddRow(
				Markup.Escape(device.Platform),
				Markup.Escape(device.Type.ToString()),
				$"[{stateColor}]{Markup.Escape(device.State.ToString())}[/]",
				Markup.Escape(device.Id),
				Markup.Escape(device.Name));
		}

		_console.Write(table);
	}

	public void WriteError(Exception exception)
	{
		var errorResult = ErrorResult.FromException(exception);
		if (exception is not Errors.MauiToolException)
		{
			_console.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
		}
		else
		{
			WriteError(errorResult);
		}
	}

	public void WriteError(ErrorResult error)
	{
		_console.MarkupLine($"[red]Error [[{Markup.Escape(error.Code)}]]:[/] {Markup.Escape(error.Message)}");

		if (!string.IsNullOrWhiteSpace(error.NativeError))
		{
			_console.MarkupLine($"  [grey]{Markup.Escape(error.NativeError.Trim())}[/]");
		}

		if (error.Remediation != null)
		{
			if (error.Remediation.Command != null)
			{
				_console.MarkupLine($"  [yellow]Fix:[/] [blue]{Markup.Escape(error.Remediation.Command)}[/]");
			}
			else if (error.Remediation.ManualSteps != null)
			{
				_console.MarkupLine("  [yellow]Manual steps required:[/]");
				foreach (var step in error.Remediation.ManualSteps)
				{
					_console.MarkupLine($"    [grey]•[/] {Markup.Escape(step)}");
				}
			}
		}
	}

	public void WriteSuccess(string message)
	{
		_console.MarkupLine($"[green]✓[/] {Markup.Escape(message)}");
	}

	public void WriteWarning(string message)
	{
		_console.MarkupLine($"[yellow]⚠[/] {Markup.Escape(message)}");
	}

	public void WriteInfo(string message)
	{
		_console.MarkupLine($"[cyan]ℹ[/] {Markup.Escape(message)}");
	}

	public void WriteProgress(string message, int? percentage = null)
	{
		if (percentage.HasValue)
		{
			_console.MarkupLine($"  [grey][[{percentage,3}%]][/] {Markup.Escape(message)}");
		}
		else
		{
			_console.MarkupLine($"  {Markup.Escape(message)}");
		}
	}

	/// <summary>
	/// Writes a pre-formatted Spectre markup line (no escaping).
	/// </summary>
	public void WriteMarkupLine(string markup)
	{
		_console.MarkupLine(markup);
	}

	/// <summary>
	/// Shows an interactive Spectre prompt (SelectionPrompt, TextPrompt, etc.).
	/// </summary>
	public T Prompt<T>(IPrompt<T> prompt)
	{
		return _console.Prompt(prompt);
	}

	public void WriteTable<T>(IEnumerable<T> items, params (string Header, Func<T, string> Selector)[] columns)
	{
		var itemsList = items.ToList();
		if (itemsList.Count == 0)
			return;

		var table = new Table().Border(TableBorder.Rounded);

		foreach (var col in columns)
		{
			table.AddColumn(new TableColumn($"[cyan]{Markup.Escape(col.Header)}[/]"));
		}

		foreach (var item in itemsList)
		{
			var cells = columns.Select(c => Markup.Escape(c.Selector(item) ?? "")).ToArray();
			table.AddRow(cells);
		}

		_console.Write(table);
	}

	/// <summary>
	/// Runs an async operation with a spinner indicator.
	/// </summary>
	public async Task<T> StatusAsync<T>(string message, Func<StatusContext, Task<T>> operation)
	{
		return await RunTimedStatusAsync(message, (_, ctx) => operation(ctx));
	}

	/// <summary>
	/// Runs an async operation with a spinner indicator and a callback for updating the displayed status text.
	/// </summary>
	public async Task<T> StatusAsync<T>(string message, Func<Action<string>, Task<T>> operation)
	{
		return await RunTimedStatusAsync(message, (updateStatus, _) => operation(updateStatus));
	}

	/// <summary>
	/// Runs an async operation with a spinner indicator.
	/// </summary>
	public async Task<T> StatusAsync<T>(string message, Func<Task<T>> operation)
	{
		return await RunTimedStatusAsync(message, (_, _) => operation());
	}

	/// <summary>
	/// Runs an async operation with a spinner indicator.
	/// </summary>
	public async Task StatusAsync(string message, Func<StatusContext, Task> operation)
	{
		await RunTimedStatusAsync(
			message,
			async (_, ctx) =>
			{
				await operation(ctx);
				return true;
			});
	}

	/// <summary>
	/// Runs an async operation with a spinner indicator and a callback for updating the displayed status text.
	/// </summary>
	public async Task StatusAsync(string message, Func<Action<string>, Task> operation)
	{
		await RunTimedStatusAsync(
			message,
			async (updateStatus, _) =>
			{
				await operation(updateStatus);
				return true;
			});
	}

	/// <summary>
	/// Runs an async operation with a spinner indicator.
	/// </summary>
	public async Task StatusAsync(string message, Func<Task> operation)
	{
		await RunTimedStatusAsync(
			message,
			async (_, _) =>
			{
				await operation();
				return true;
			});
	}

	async Task<T> RunTimedStatusAsync<T>(string message, Func<Action<string>, StatusContext, Task<T>> operation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		var stopwatch = Stopwatch.StartNew();
		var currentMarkup = Markup.Escape(message);
		T result = default!;

		await _console.Status()
			.Spinner(Spinner.Known.Dots)
			.SpinnerStyle(Style.Parse("cyan"))
			.StartAsync(FormatTimedStatusMarkup(currentMarkup, stopwatch.Elapsed), async ctx =>
			{
				using var refreshCts = new CancellationTokenSource();
				var refreshTask = RefreshStatusAsync(ctx, () => currentMarkup, stopwatch, refreshCts.Token);

				void UpdateStatus(string updatedMarkup)
				{
					if (string.IsNullOrWhiteSpace(updatedMarkup))
						return;

					currentMarkup = updatedMarkup;
					ctx.Status(FormatTimedStatusMarkup(currentMarkup, stopwatch.Elapsed));
				}

				try
				{
					result = await operation(UpdateStatus, ctx);
					UpdateStatus(currentMarkup);
				}
				finally
				{
					refreshCts.Cancel();
					try
					{
						await refreshTask;
					}
					catch (OperationCanceledException)
					{
					}
				}
			});

		WriteInfo(FormatCompletedStatusMessage(message, stopwatch.Elapsed));
		return result;
	}

	static async Task RefreshStatusAsync(
		StatusContext context,
		Func<string> getCurrentMarkup,
		Stopwatch stopwatch,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			await Task.Delay(s_statusRefreshInterval, cancellationToken);
			context.Status(FormatTimedStatusMarkup(getCurrentMarkup(), stopwatch.Elapsed));
		}
	}

	internal static string FormatElapsed(TimeSpan elapsed)
	{
		if (elapsed.TotalMinutes >= 1)
		{
			var totalMinutes = (int)elapsed.TotalMinutes;
			var tenths = elapsed.Milliseconds / 100;
			return $"{totalMinutes}:{elapsed.Seconds:00}.{tenths:0}s";
		}

		return $"{elapsed.TotalSeconds:0.0}s";
	}

	internal static string FormatTimedStatusMarkup(string statusMarkup, TimeSpan elapsed)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(statusMarkup);

		var suffix = $" [grey]({FormatElapsed(elapsed)})[/]";
		var lineBreakIndex = statusMarkup.IndexOfAny(['\r', '\n']);
		return lineBreakIndex >= 0
			? statusMarkup.Insert(lineBreakIndex, suffix)
			: statusMarkup + suffix;
	}

	internal static string FormatCompletedStatusMessage(string message, TimeSpan elapsed) =>
		$"{TrimCompletedStatusLabel(message)} ({FormatElapsed(elapsed)})";

	internal static string TrimCompletedStatusLabel(string message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		var trimmed = message.Trim();
		return trimmed.EndsWith("...", StringComparison.Ordinal)
			? trimmed[..^3].TrimEnd()
			: trimmed.TrimEnd('.');
	}

	/// <summary>
	/// Runs an operation with a progress bar.
	/// </summary>
	public async Task ProgressAsync(string description, Func<Action<double>, Task> operation)
	{
		await _console.Progress()
			.AutoClear(false)
			.Columns(
				new TaskDescriptionColumn(),
				new ProgressBarColumn(),
				new PercentageColumn(),
				new SpinnerColumn())
			.StartAsync(async ctx =>
			{
				var task = ctx.AddTask(description, maxValue: 100);
				await operation(value => task.Value = value);
			});
	}

	/// <summary>
	/// Runs a multi-step operation with individual progress bars per step.
	/// Each step gets its own progress bar that updates in-place (no line spam).
	/// </summary>
	public async Task LiveProgressAsync(Func<ILiveProgressContext, Task> operation)
	{
		await _console.Progress()
			.AutoClear(false)
			.HideCompleted(false)
			.Columns(
				new TaskDescriptionColumn(),
				new ProgressBarColumn(),
				new PercentageColumn(),
				new DownloadedColumn(),
				new SpinnerColumn())
			.StartAsync(async ctx =>
			{
				var liveContext = new SpectreLiveProgressContext(ctx);
				await operation(liveContext);
			});
	}

	/// <summary>
	/// Writes a Spectre-formatted version information block.
	/// </summary>
	public void WriteVersion(string version, string runtime, string os)
	{
		var grid = new Grid()
			.AddColumn(new GridColumn().NoWrap())
			.AddColumn();

		grid.AddRow("[cyan]MAUI DevTools[/]", $"[bold]v{Markup.Escape(version)}[/]");
		grid.AddRow("[grey]Runtime[/]", Markup.Escape(runtime));
		grid.AddRow("[grey]OS[/]", Markup.Escape(os));

		_console.Write(grid);
	}
}

/// <summary>
/// Context for a multi-step progress operation. Provides named tasks that update in-place.
/// </summary>
public interface ILiveProgressContext
{
	/// <summary>
	/// Adds or retrieves a named progress task.
	/// </summary>
	ILiveProgressTask AddTask(string description, double maxValue = 100);

	/// <summary>
	/// Creates a progress reporter that maps structured progress to a task.
	/// </summary>
	IProgress<(double percent, string message)> CreateDownloadProgress(string description, double totalBytes);
}

/// <summary>
/// A single progress task in a live progress context.
/// </summary>
public interface ILiveProgressTask
{
	void Update(double value, string? description = null);
	void Complete(string? description = null);
	void SetIndeterminate(string? description = null);
}

internal sealed class SpectreLiveProgressContext : ILiveProgressContext
{
	readonly ProgressContext _ctx;
	readonly Dictionary<string, (ProgressTask Task, ILiveProgressTask Wrapper)> _tasks = new();

	public SpectreLiveProgressContext(ProgressContext ctx)
	{
		_ctx = ctx;
	}

	public ILiveProgressTask AddTask(string description, double maxValue = 100)
	{
		if (_tasks.TryGetValue(description, out var existing))
			return existing.Wrapper;

		var task = _ctx.AddTask(Markup.Escape(description), maxValue: maxValue);
		var wrapper = new SpectreLiveProgressTask(task);
		_tasks[description] = (task, wrapper);
		return wrapper;
	}

	public IProgress<(double percent, string message)> CreateDownloadProgress(string description, double totalBytes)
	{
		var task = _ctx.AddTask($"[cyan]↓[/] {Markup.Escape(description)}", maxValue: 100);
		var wrapper = new SpectreLiveProgressTask(task);
		_tasks[description] = (task, wrapper);

		return new Progress<(double percent, string message)>(p =>
		{
			task.Value = Math.Min(p.percent, 100);
		});
	}
}

internal sealed class SpectreLiveProgressTask : ILiveProgressTask
{
	readonly ProgressTask _task;

	public SpectreLiveProgressTask(ProgressTask task)
	{
		_task = task;
	}

	public void Update(double value, string? description = null)
	{
		_task.IsIndeterminate = false;
		_task.Value = Math.Min(value, _task.MaxValue);
		if (description != null)
			_task.Description = Markup.Escape(description);
	}

	public void Complete(string? description = null)
	{
		if (description != null)
			_task.Description = $"[green]✓[/] {Markup.Escape(description)}";
		_task.Value = _task.MaxValue;
		_task.StopTask();
	}

	public void SetIndeterminate(string? description = null)
	{
		_task.IsIndeterminate = true;
		if (description != null)
			_task.Description = Markup.Escape(description);
	}
}
