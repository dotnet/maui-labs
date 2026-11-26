namespace Browser.Essentials.Tests;

/// <summary>
/// Asserts the results of the in-browser Essentials test suite
/// (see Browser.Essentials.TestApp/EssentialsTestSuite.cs), which runs inside the
/// WebAssembly runtime against real browser APIs in headless Chromium.
/// </summary>
public class EssentialsInBrowserTests : IClassFixture<TestAppFixture>
{
	readonly TestAppFixture fixture;

	public EssentialsInBrowserTests(TestAppFixture fixture) => this.fixture = fixture;

	[BrowserTestsFact]
	public void SuiteRanExpectedNumberOfTests()
	{
		Assert.True(fixture.Results.Count >= 20,
			$"Expected at least 20 in-browser tests, got {fixture.Results.Count}.");
	}

	[BrowserTestsFact]
	public void AllInBrowserTestsPass()
	{
		var failures = fixture.Results.Where(r => !r.Passed).ToList();
		Assert.True(failures.Count == 0,
			"In-browser test failures:\n" + string.Join("\n", failures.Select(f => $"  {f.Name}: {f.Error}")));
	}

	[BrowserTestsFact]
	public async Task AnnounceCreatedAriaLiveRegion()
	{
		// The suite calls ISemanticScreenReader.Announce, which lazily creates the region.
		var region = await fixture.Page.QuerySelectorAsync("[aria-live='polite']");
		Assert.NotNull(region);
	}
}
