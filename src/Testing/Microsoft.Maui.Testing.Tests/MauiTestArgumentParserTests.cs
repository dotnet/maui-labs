namespace Microsoft.Maui.Testing.Tests;

[TestClass]
public sealed class MauiTestArgumentParserTests
{
    [TestMethod]
    public void Parse_QuotedFilter_PreservesArgumentBoundaries()
    {
        var arguments = MauiTestArgumentParser.Parse(
            "--filter \"FullyQualifiedName~Smoke Test\" --report-trx");

        CollectionAssert.AreEqual(
            new[] { "--filter", "FullyQualifiedName~Smoke Test", "--report-trx" },
            arguments);
    }

    [TestMethod]
    public void Parse_UnmatchedQuote_ThrowsFormatException()
    {
        Assert.ThrowsExactly<FormatException>(() =>
            MauiTestArgumentParser.Parse("--filter \"unterminated"));
    }

    [TestMethod]
    public void Parse_EmptyQuotedArgument_PreservesArgument()
    {
        var arguments = MauiTestArgumentParser.Parse("--filter \"\" --report-trx");

        CollectionAssert.AreEqual(
            new[] { "--filter", string.Empty, "--report-trx" },
            arguments);
    }
}
