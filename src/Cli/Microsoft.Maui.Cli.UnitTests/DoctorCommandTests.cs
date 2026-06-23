// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DoctorCommandTests
{
	[Fact]
	public async Task DoctorFix_FixableIssue_RunsFixAndRerunsChecks()
	{
		var fix = new FixInfo
		{
			IssueId = "E2402",
			Description = "Install MAUI workload",
			AutoFixable = true,
			Command = "dotnet workload install maui"
		};
		var service = new FakeDoctorService(
			[
				CreateReport(new HealthCheck
				{
					Category = "dotnet",
					Name = "MAUI Workload",
					Status = CheckStatus.Error,
					Message = "MAUI workload not installed",
					Fix = fix
				}),
				CreateReport(new HealthCheck
				{
					Category = "dotnet",
					Name = "MAUI Workload",
					Status = CheckStatus.Ok,
					Message = "MAUI workload installed"
				})
			]);

		var exitCode = await InvokeDoctorAsync(service, "--json", "--fix");

		Assert.Equal(0, exitCode);
		Assert.Equal(2, service.RunAllChecksCallCount);
		Assert.Single(service.FixesAttempted);
		Assert.Same(fix, service.FixesAttempted[0]);
	}

	[Fact]
	public async Task DoctorFix_NonFixableIssue_DoesNotAttemptFix()
	{
		var service = new FakeDoctorService(
			[
				CreateReport(new HealthCheck
				{
					Category = "dotnet",
					Name = ".NET SDK",
					Status = CheckStatus.Error,
					Message = ".NET SDK not found",
					Fix = new FixInfo
					{
						IssueId = "E2401",
						Description = "Install .NET SDK",
						AutoFixable = false,
						ManualSteps = ["Download and install .NET SDK from https://dot.net/download"]
					}
				})
			]);

		var exitCode = await InvokeDoctorAsync(service, "--json", "--fix");

		Assert.Equal(1, exitCode);
		Assert.Equal(1, service.RunAllChecksCallCount);
		Assert.Empty(service.FixesAttempted);
	}

	[Fact]
	public async Task DoctorFix_FixDoesNotResolveIssue_ReturnsFailureAfterRerun()
	{
		var service = new FakeDoctorService(
			[
				CreateMissingWorkloadReport(),
				CreateMissingWorkloadReport()
			]);

		var exitCode = await InvokeDoctorAsync(service, "--json", "--fix");

		Assert.Equal(1, exitCode);
		Assert.Equal(2, service.RunAllChecksCallCount);
		Assert.Single(service.FixesAttempted);
	}

	[Fact]
	public async Task DoctorFix_PlatformScoped_RerunsCategoryChecksAfterFix()
	{
		var service = new FakeDoctorService(
			[
				CreateMissingWorkloadReport(),
				CreateReport(new HealthCheck
				{
					Category = "dotnet",
					Name = "MAUI Workload",
					Status = CheckStatus.Ok,
					Message = "MAUI workload installed"
				})
			]);

		var exitCode = await InvokeDoctorAsync(service, "--json", "--fix", "--platform", "dotnet");

		Assert.Equal(0, exitCode);
		Assert.Equal(2, service.RunCategoryChecksCallCount);
		Assert.Equal(["dotnet", "dotnet"], service.CategoriesChecked);
		Assert.Equal(0, service.RunAllChecksCallCount);
		Assert.Single(service.FixesAttempted);
	}

	static async Task<int> InvokeDoctorAsync(FakeDoctorService doctorService, params string[] args)
	{
		var testProvider = ServiceConfiguration.CreateTestServiceProvider(doctorService: doctorService);
		try
		{
			Program.Services = testProvider;

			var rootCommand = Program.BuildRootCommand();
			var parseResult = rootCommand.Parse(["doctor", .. args]);
			return await parseResult.InvokeAsync();
		}
		finally
		{
			Program.ResetServices();
		}
	}

	static DoctorReport CreateMissingWorkloadReport() =>
		CreateReport(new HealthCheck
		{
			Category = "dotnet",
			Name = "MAUI Workload",
			Status = CheckStatus.Error,
			Message = "MAUI workload not installed",
			Fix = new FixInfo
			{
				IssueId = "E2402",
				Description = "Install MAUI workload",
				AutoFixable = true,
				Command = "dotnet workload install maui"
			}
		});

	static DoctorReport CreateReport(params HealthCheck[] checks)
	{
		var errorCount = checks.Count(check => check.Status == CheckStatus.Error);
		var warningCount = checks.Count(check => check.Status == CheckStatus.Warning);
		var okCount = checks.Count(check => check.Status == CheckStatus.Ok);

		return new DoctorReport
		{
			CorrelationId = "test",
			Timestamp = DateTime.UtcNow,
			Status = errorCount > 0
				? HealthStatus.Unhealthy
				: warningCount > 0 ? HealthStatus.Degraded : HealthStatus.Healthy,
			Checks = checks.ToList(),
			Summary = new DoctorSummary
			{
				Total = checks.Length,
				Ok = okCount,
				Warning = warningCount,
				Error = errorCount
			}
		};
	}

	sealed class FakeDoctorService : IDoctorService
	{
		readonly Queue<DoctorReport> _reports;

		public FakeDoctorService(IEnumerable<DoctorReport> reports)
		{
			_reports = new Queue<DoctorReport>(reports);
		}

		public int RunAllChecksCallCount { get; private set; }

		public int RunCategoryChecksCallCount { get; private set; }

		public List<string> CategoriesChecked { get; } = new();

		public List<FixInfo> FixesAttempted { get; } = new();

		public Task<DoctorReport> RunAllChecksAsync(CancellationToken cancellationToken = default)
		{
			RunAllChecksCallCount++;
			return Task.FromResult(_reports.Count > 1 ? _reports.Dequeue() : _reports.Peek());
		}

		public Task<DoctorReport> RunCategoryChecksAsync(string category, CancellationToken cancellationToken = default)
		{
			RunCategoryChecksCallCount++;
			CategoriesChecked.Add(category);
			return Task.FromResult(_reports.Count > 1 ? _reports.Dequeue() : _reports.Peek());
		}

		public Task<bool> TryFixAsync(FixInfo fix, CancellationToken cancellationToken = default)
		{
			FixesAttempted.Add(fix);
			return Task.FromResult(true);
		}
	}
}
