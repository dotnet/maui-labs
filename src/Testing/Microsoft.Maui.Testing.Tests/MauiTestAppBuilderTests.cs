using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.Testing.Tests;

[TestClass]
public sealed class MauiTestAppBuilderTests
{
    [TestMethod]
    public void Build_WithoutTestFramework_Throws()
    {
        var builder = MauiTestApp.CreateBuilder();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => builder.Build());

        StringAssert.Contains(exception.Message, "No test framework is configured");
    }

    [TestMethod]
    public void ConfigureTestApplication_Twice_Throws()
    {
        var builder = MauiTestApp.CreateBuilder()
            .ConfigureTestApplication(_ => { });

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => builder.ConfigureTestApplication(_ => { }));

        StringAssert.Contains(exception.Message, "already been configured");
    }

    [TestMethod]
    public void ConfigureTestApplication_WithRunner_Builds()
    {
        var builder = MauiTestApp.CreateBuilder()
            .ConfigureTestApplication((_, _) => Task.FromResult(0));

        using var app = builder.Build();

        Assert.IsNotNull(app);
    }

    [TestMethod]
    public void Build_RegistersServices()
    {
        var options = new MauiTestAppOptions
        {
            ResultsDirectoryName = "CustomResults",
            GenerateTrxReport = false,
        };
        var builder = MauiTestApp.CreateBuilder(options)
            .ConfigureTestApplication(_ => { });
        builder.Services.AddSingleton(new TestService("registered"));

        using var app = builder.Build();

        Assert.AreEqual("registered", app.Services.GetRequiredService<TestService>().Value);
        Assert.AreSame(options, app.Services.GetRequiredService<MauiTestAppOptions>());
    }

    [TestMethod]
    public void CreateArguments_AddsDefaults()
    {
        var arguments = MauiTestApp.CreateArguments([], "C:\\results", generateTrxReport: true);

        CollectionAssert.AreEqual(
            new[] { "--results-directory", "C:\\results", "--report-trx" },
            arguments);
    }

    [TestMethod]
    public void CreateArguments_PreservesExplicitOptions()
    {
        string[] input = ["--results-directory", "D:\\custom", "--report-trx", "--filter", "Test1"];

        var arguments = MauiTestApp.CreateArguments(input, "C:\\results", generateTrxReport: true);

        CollectionAssert.AreEqual(input, arguments);
    }

    [TestMethod]
    [DataRow("--results-directory=D:\\custom")]
    [DataRow("--results-directory:D:\\custom")]
    public void CreateArguments_DelimitedResultsDirectory_PreservesExplicitOption(string option)
    {
        string[] input = [option];

        var arguments = MauiTestApp.CreateArguments(input, "C:\\results", generateTrxReport: false);

        CollectionAssert.AreEqual(input, arguments);
    }

    private sealed record TestService(string Value);
}
