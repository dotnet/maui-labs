using System;
using System.IO;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests
{
	public class TemplateCurrentSurfaceValidationTests
	{
		static readonly string RepoRoot = IOPath.GetFullPath(IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
		static readonly string TemplateRoot = IOPath.Combine(RepoRoot, "templates", "single-project", "CometApp1");

		[Fact]
		public void SingleProjectTemplateUsesCurrentComponentSurface()
		{
			var appSource = ReadTemplateFile("App.cs");
			var mainPageSource = ReadTemplateFile("MainPage.cs");

			Assert.Matches(@"Body\s*=\s*\(\)\s*=>\s*new\s+MainPage\s*\(\s*\)", appSource);
			Assert.Matches(@":\s*View", mainPageSource);
			Assert.Matches(@"\[Body\]", mainPageSource);
			Assert.Matches(@"\bReactive<", mainPageSource);
		}

		[Fact]
		public void SingleProjectTemplateTargetsCurrentMauiAndDropsLegacyDependencies()
		{
			var projectFile = ReadTemplateFile("CometApp1.csproj");

			Assert.Contains("net11.0-maccatalyst", projectFile, StringComparison.Ordinal);
			Assert.DoesNotContain("net7.0", projectFile, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("Reloadify3000", projectFile, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("COMET_VERSION", projectFile, StringComparison.Ordinal);
		}

		static string ReadTemplateFile(string relativePath)
		{
			var fullPath = IOPath.Combine(TemplateRoot, relativePath);
			Assert.True(File.Exists(fullPath), $"Expected template file at {fullPath}");
			return File.ReadAllText(fullPath);
		}
	}
}
