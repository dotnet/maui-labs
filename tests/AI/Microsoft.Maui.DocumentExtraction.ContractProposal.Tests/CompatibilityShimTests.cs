using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DocumentExtraction;
using Xunit;

namespace Microsoft.Maui.DocumentExtraction.ContractProposal.Tests;

public class CompatibilityShimTests
{
    [Fact]
    public void ContractTypes_ExperimentalMetadata_UsesUpstreamDiagnosticValues()
    {
        ExperimentalAttribute attribute = Assert.IsType<ExperimentalAttribute>(
            typeof(IDocumentExtractionClient).GetCustomAttribute<ExperimentalAttribute>());

        Assert.Equal("MEDE0001", attribute.DiagnosticId);
        Assert.Equal("https://aka.ms/dotnet-extensions-warnings/{0}", attribute.UrlFormat);
    }

    [Fact]
    public void DocumentBlock_NullText_UsesIfNullCompatibilityShim()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new DocumentBlock(null!));

        Assert.Equal("text", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void DocumentBlockKind_WhitespaceValue_UsesIfNullOrWhitespaceCompatibilityShim(string value)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new DocumentBlockKind(value));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void DocumentBlockKind_NullValue_UsesIfNullOrWhitespaceCompatibilityShim()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new DocumentBlockKind(null!));

        Assert.Equal("value", exception.ParamName);
    }
}
