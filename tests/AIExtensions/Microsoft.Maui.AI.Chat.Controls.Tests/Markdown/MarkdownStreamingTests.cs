using Microsoft.Maui.AI.Chat;
using static Microsoft.Maui.AI.Chat.Controls.Tests.Markdown.MarkdownTestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.Markdown;

public class MarkdownStreamingTests
{
    // A typical model answer exercising most constructs. Fence info strings are single words and
    // ordered items are numbered sequentially, so every letter and digit is recoverable.
    internal const string LlmAnswer = """
        # Getting started with .NET MAUI

        .NET MAUI lets you build **native** apps for *Android*, _iOS_, macOS and Windows from a single codebase.
        See the [official docs](https://learn.microsoft.com/dotnet/maui "MAUI docs") or https://github.com/dotnet/maui_samples for details.

        ## Prerequisites

        1. Install the .NET 10 SDK
        2. Install the workload:
           ```bash
           dotnet workload install maui
           ```
        3. Pick an editor
           - Visual Studio 2026
           - VS Code with the `C# Dev Kit` extension
             - optional: the **MAUI** extension
        4. Verify with `dotnet --info`

        > **Tip:** run `dotnet new maui -n MyApp` to scaffold a project.
        It works on every platform.

        ### Platform support

        | Platform | Minimum version | Notes |
        |:---------|:---------------:|------:|
        | Android  | API 24          | `net10.0-android` |
        | iOS      | 15.0            | Requires a Mac |
        | Windows  | 10.0.17763      | WinUI 3 \| WinAppSDK |

        ## Example

        ```csharp
        public static class MauiProgram
        {
            public static MauiApp CreateMauiApp()
            {
                var builder = MauiApp.CreateBuilder();
                builder.UseMauiApp<App>(); // snake_case_stays_literal
                return builder.Build();
            }
        }
        ```

        - [x] Create the project
        - [ ] Add a page
        - Read about ~~Xamarin.Forms~~ migration_notes and __really__ important things.

        ---

        Questions? Email <support@example.com> or open <https://github.com/dotnet/maui/issues>.
        ![MAUI logo](https://example.com/logo.png "Logo")
        """;

    [Fact]
    public void RepresentativeAnswer_ParsesIntoTheExpectedBlocks()
    {
        var nodes = MarkdownRichTextParser.ParseWithoutFallback(LlmAnswer);

        Assert.Collection(
            nodes,
            node => Assert.Equal(1, Assert.IsType<HeadingNode>(node).Level),
            node => Assert.IsType<ParagraphNode>(node),
            node => Assert.Equal(2, Assert.IsType<HeadingNode>(node).Level),
            node => Assert.True(Assert.IsType<ListNode>(node).Ordered),
            node => Assert.IsType<BlockQuoteNode>(node),
            node => Assert.Equal(3, Assert.IsType<HeadingNode>(node).Level),
            node => Assert.Equal(4, Assert.IsType<TableNode>(node).Children.Count),
            node => Assert.Equal(2, Assert.IsType<HeadingNode>(node).Level),
            node => Assert.Equal("csharp", Assert.IsType<CodeBlockNode>(node).Language),
            node => Assert.Equal(3, Assert.IsType<ListNode>(node).Children.Count),
            node => Assert.IsType<ThematicBreakNode>(node),
            node => Assert.IsType<ParagraphNode>(node));

        var list = (ListNode)nodes[3];
        Assert.Equal(4, list.Children.Count);
        Assert.IsType<CodeBlockNode>(list.Children[1].Children[1]);
        var editors = Assert.IsType<ListNode>(list.Children[2].Children[1]);
        Assert.IsType<ListNode>(editors.Children[1].Children[1]);

        var quote = (BlockQuoteNode)nodes[4];
        Assert.Equal(
            "p(strong(\"Tip:\") \" run \" code\"dotnet new maui -n MyApp\" \" to scaffold a project.\" br \"It works on every platform.\")",
            Dump(quote.Children));

        var table = (TableNode)nodes[6];
        Assert.Equal(
            [TableColumnAlignment.Left, TableColumnAlignment.Center, TableColumnAlignment.Right],
            table.Alignment);
        Assert.Equal("td(\"WinUI 3 | WinAppSDK\")", Dump([table.Children[3].Children[2]]));

        var tasks = (ListNode)nodes[9];
        Assert.Equal(true, ((ListItemNode)tasks.Children[0]).Checked);
        Assert.Equal(false, ((ListItemNode)tasks.Children[1]).Checked);
        Assert.Null(((ListItemNode)tasks.Children[2]).Checked);
        Assert.Contains("migration_notes", Dump(tasks.Children[2].Children));
    }

    [Fact]
    public void EveryPrefix_ParsesWithoutThrowingOrLosingText()
    {
        for (var length = 0; length <= LlmAnswer.Length; length++)
        {
            var prefix = LlmAnswer[..length];
            var nodes = MarkdownRichTextParser.ParseWithoutFallback(prefix);

            var expected = MarkdownRichTextParserTests.LettersAndDigits(prefix);
            var actual = MarkdownRichTextParserTests.LettersAndDigits(GetVisibleText(nodes));
            Assert.True(
                MarkdownRichTextParserTests.IsSubsequence(expected, actual),
                $"Prefix of length {length} lost text.\nPrefix: {prefix}\nVisible: {GetVisibleText(nodes)}");
        }
    }

    [Fact]
    public void EveryPrefix_WithWindowsLineEndings_ParsesWithoutThrowing()
    {
        var markdown = LlmAnswer.ReplaceLineEndings("\r\n");
        for (var length = 0; length <= markdown.Length; length++)
            MarkdownRichTextParser.ParseWithoutFallback(markdown[..length]);
    }

    [Fact]
    public void UnclosedFence_RendersAsACodeBlockWhileStreaming()
    {
        const string Fence = "```csharp\n";
        var answer = LlmAnswer.ReplaceLineEndings("\n");
        var start = answer.IndexOf(Fence, StringComparison.Ordinal) + Fence.Length;
        var end = answer.IndexOf("```", start, StringComparison.Ordinal);

        // Through the partial closing fences "`" and "``", which are still code.
        for (var length = start; length <= end + 2; length++)
        {
            var nodes = MarkdownRichTextParser.ParseWithoutFallback(answer[..length]);

            var code = Assert.IsType<CodeBlockNode>(nodes[^1]);
            Assert.Equal("csharp", code.Language);
            var streamed = answer[start..length];
            Assert.Equal(streamed.EndsWith('\n') ? streamed[..^1] : streamed, code.Code);
        }
    }

    [Fact]
    public void FiftyKilobyteDocument_Parses()
    {
        var repetitions = (50 * 1024 / LlmAnswer.Length) + 1;
        var markdown = string.Join("\n\n", Enumerable.Repeat(LlmAnswer, repetitions));
        Assert.True(markdown.Length >= 50 * 1024);

        var nodes = MarkdownRichTextParser.Parse(markdown);

        Assert.Equal(12 * repetitions, nodes.Count);
        Assert.Equal(
            Dump(MarkdownRichTextParser.Parse(LlmAnswer)),
            Dump(nodes.Take(12).ToList()));
    }

    [Fact]
    public void StreamingTail_ChangesOnlyTheLastTwoBlocks()
    {
        // As an answer streams, blocks before the last two are final, which is what lets the view
        // keep their views. (A partial line such as "3" is its own paragraph until "3." joins
        // the preceding list, so the block before the last one can still change.)
        var previous = MarkdownRichTextParser.Parse(string.Empty);
        for (var length = 1; length <= LlmAnswer.Length; length++)
        {
            var current = MarkdownRichTextParser.Parse(LlmAnswer[..length]);
            for (var i = 0; i < previous.Count - 2; i++)
            {
                Assert.True(
                    RichTextNodeComparer.AreEqual(previous[i], current[i]),
                    $"Block {i} changed at length {length}: {Dump([previous[i]])} -> {Dump([current[i]])}");
            }

            previous = current;
        }
    }
}
