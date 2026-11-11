using Microsoft.Maui.AI.Chat;
using static Microsoft.Maui.AI.Chat.Controls.Tests.Markdown.MarkdownTestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.Markdown;

// Maps Markdig's syntax tree onto the rich-text node model. The expectations use the compact
// Dump format from MarkdownTestHelpers, e.g. p("text" br strong("bold")).
public class MarkdownRichTextParserTests
{
    [Theory]
    [InlineData("# One", "h1(\"One\")")]
    [InlineData("###### Six", "h6(\"Six\")")]
    [InlineData("####### Seven", "p(\"####### Seven\")")]
    [InlineData("## Title ##", "h2(\"Title\")")]
    [InlineData("# C#", "h1(\"C#\")")]
    [InlineData("#hashtag", "p(\"#hashtag\")")]
    [InlineData("#", "h1()")]
    [InlineData("# **Bold** title", "h1(strong(\"Bold\") \" title\")")]
    [InlineData("Setext\n===", "h1(\"Setext\")")]
    [InlineData("Setext two\n---", "h2(\"Setext two\")")]
    public void Headings(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("a\nb", "p(\"a\" br \"b\")")]
    [InlineData("a\n\nb", "p(\"a\") p(\"b\")")]
    [InlineData("a\r\nb\r\n\r\nc", "p(\"a\" br \"b\") p(\"c\")")]
    [InlineData("  indented\n    lines", "p(\"indented\" br \"lines\")")]
    [InlineData("trailing spaces   ", "p(\"trailing spaces\")")]
    [InlineData("two spaces  \nbreak", "p(\"two spaces\" br \"break\")")]
    [InlineData("backslash\\\nbreak", "p(\"backslash\" br \"break\")")]
    [InlineData("ends with backslash\\", "p(\"ends with backslash\\\")")]
    public void Paragraphs_TurnEveryNewlineIntoALineBreak(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("```csharp\nvar x = 1;\n```", "pre[csharp]\"var x = 1;\"")]
    [InlineData("~~~\ncode\n~~~", "pre\"code\"")]
    [InlineData("```js title=\"app.js\"\nlet a;\n```", "pre[js]\"let a;\"")]
    [InlineData("```\n  indented\n\n**not bold** <b>\n```", "pre\"  indented\n\n**not bold** <b>\"")]
    [InlineData("````\n```\ninner\n```\n````", "pre\"```\ninner\n```\"")]
    [InlineData("```py\nx\n```\nafter", "pre[py]\"x\" p(\"after\")")]
    [InlineData("text\n```\ncode\n```", "p(\"text\") pre\"code\"")]
    [InlineData("    indented code\n    more", "pre\"indented code\nmore\"")]
    public void CodeBlocks_AreVerbatimWithTheirLanguage(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("```python\nprint('hi')", "pre[python]\"print('hi')\"")]
    [InlineData("```python\nprint('hi')\n", "pre[python]\"print('hi')\"")]
    [InlineData("```", "pre\"\"")]
    [InlineData("```py", "pre[py]\"\"")]
    [InlineData("~~~\na\n\nb", "pre\"a\n\nb\"")]
    [InlineData("```\ncode\n``", "pre\"code\n``\"")]
    public void UnclosedFence_IsACodeBlockWithEverythingStreamedSoFar(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("> quote", "quote(p(\"quote\"))")]
    [InlineData("> a\nlazy", "quote(p(\"a\" br \"lazy\"))")]
    [InlineData("> a\n>\n> b", "quote(p(\"a\") p(\"b\"))")]
    [InlineData("> > nested\n> back", "quote(quote(p(\"nested\" br \"back\")))")]
    [InlineData("> - item\n> - two", "quote(ul(li(p(\"item\")) li(p(\"two\"))))")]
    [InlineData("> ```cs\n> var x = 1;\n> ```", "quote(pre[cs]\"var x = 1;\")")]
    [InlineData(
        "> 1. first\n>    ```\n>    code\n>    ```",
        "quote(ol1(li(p(\"first\") pre\"code\")))")]
    public void BlockQuotes_ContainBlocks(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("- a\n- b", "ul(li(p(\"a\")) li(p(\"b\")))")]
    [InlineData("* a\n* b", "ul(li(p(\"a\")) li(p(\"b\")))")]
    [InlineData("+ a\n+ b", "ul(li(p(\"a\")) li(p(\"b\")))")]
    [InlineData("- a\n+ b", "ul(li(p(\"a\"))) ul(li(p(\"b\")))")]
    [InlineData("1. a\n2. b", "ol1(li(p(\"a\")) li(p(\"b\")))")]
    [InlineData("3) a\n4) b", "ol3(li(p(\"a\")) li(p(\"b\")))")]
    [InlineData("1. a\n1) b", "ol1(li(p(\"a\"))) ol1(li(p(\"b\")))")]
    [InlineData("-", "ul(li())")]
    [InlineData("- a\n  continued\n- b", "ul(li(p(\"a\" br \"continued\")) li(p(\"b\")))")]
    [InlineData("- a\ncontinued lazily", "ul(li(p(\"a\" br \"continued lazily\")))")]
    [InlineData("- a\n\n  second paragraph\n- b", "ul(li(p(\"a\") p(\"second paragraph\")) li(p(\"b\")))")]
    [InlineData("- a\n\nafter", "ul(li(p(\"a\"))) p(\"after\")")]
    [InlineData("Text\n- a list", "p(\"Text\") ul(li(p(\"a list\")))")]
    [InlineData("Text\n2. not a list\n1. a list", "p(\"Text\" br \"2. not a list\") ol1(li(p(\"a list\")))")]
    public void Lists(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("- a\n  - b\n    - c\n- d", "ul(li(p(\"a\") ul(li(p(\"b\") ul(li(p(\"c\")))))) li(p(\"d\")))")]
    [InlineData("1. First\n   - three spaces\n2. Second", "ol1(li(p(\"First\") ul(li(p(\"three spaces\")))) li(p(\"Second\")))")]
    [InlineData(
        "1. Step\n   1. Sub step\n   2. Another\n2. Next",
        "ol1(li(p(\"Step\") ol1(li(p(\"Sub step\")) li(p(\"Another\")))) li(p(\"Next\")))")]
    [InlineData(
        "1. Run:\n   ```bash\n   npm install\n   ```\n2. Done",
        "ol1(li(p(\"Run:\") pre[bash]\"npm install\") li(p(\"Done\")))")]
    public void NestedLists_AndBlocksInsideItems(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("- [ ] todo\n- [x] done\n- [X] Done", "ul(li[ ](p(\"todo\")) li[x](p(\"done\")) li[x](p(\"Done\")))")]
    [InlineData("1. [x] ordered task", "ol1(li[x](p(\"ordered task\")))")]
    [InlineData("- [ ]", "ul(li[ ](p()))")]
    [InlineData("- [x] **bold** task", "ul(li[x](p(strong(\"bold\") \" task\")))")]
    [InlineData("- [y] not a task", "ul(li(p(\"[y] not a task\")))")]
    [InlineData("- foo [x] bar", "ul(li(p(\"foo [x] bar\")))")]
    [InlineData("- [", "ul(li(p(\"[\")))")]
    public void TaskItems_UseTheCheckedState(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData(
        "| Name | Qty | Price |\n|:-----|:---:|------:|\n| Apple | 2 | $1 |",
        "table[L,C,R](tr(td(\"Name\") td(\"Qty\") td(\"Price\")) tr(td(\"Apple\") td(\"2\") td(\"$1\")))")]
    [InlineData(
        "a | b\n--- | ---\n1 | 2",
        "table[N,N](tr(td(\"a\") td(\"b\")) tr(td(\"1\") td(\"2\")))")]
    [InlineData(
        "| **b** | `c` | [l](https://x.dev) |\n|---|---|---|",
        "table[N,N,N](tr(td(strong(\"b\")) td(code\"c\") td(a[https://x.dev](\"l\"))))")]
    [InlineData(
        "| a \\| b | `a|b` |\n|---|---|",
        "table[N,N](tr(td(\"a | b\") td(code\"a|b\")))")]
    [InlineData(
        "| a | b |\n|---|---|\n| 1 |\n| 1 | 2 | 3 |",
        "table[N,N](tr(td(\"a\") td(\"b\") td()) tr(td(\"1\") td() td()) tr(td(\"1\") td(\"2\") td(\"3\")))")]
    [InlineData(
        "Intro\n| a | b |\n| - | - |\n| 1 | 2 |",
        "p(\"Intro\") table[N,N](tr(td(\"a\") td(\"b\")) tr(td(\"1\") td(\"2\")))")]
    [InlineData(
        "| a | b |\n|---|---|\n| 1 | 2 |\n\nafter",
        "table[N,N](tr(td(\"a\") td(\"b\")) tr(td(\"1\") td(\"2\"))) p(\"after\")")]
    [InlineData(
        "- | a | b |\n  |---|---|\n  | 1 | 2 |",
        "ul(li(table[N,N](tr(td(\"a\") td(\"b\")) tr(td(\"1\") td(\"2\")))))")]
    public void Tables_MapRowsCellsAndAlignment(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("| a | b |", "p(\"| a | b |\")")]
    [InlineData("| a | b |\n", "p(\"| a | b |\")")]
    [InlineData("| a | b |\n| -", "p(\"| a | b |\" br \"| -\")")]
    public void TableHeader_WithoutItsDelimiterRow_StaysAParagraph(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("---", "hr")]
    [InlineData("***", "hr")]
    [InlineData("___", "hr")]
    [InlineData(" * * * ", "hr")]
    [InlineData("a\n\n---\n\nb", "p(\"a\") hr p(\"b\")")]
    public void ThematicBreaks(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("*em* _em_ **strong** __strong__ ~~del~~",
        "p(em(\"em\") \" \" em(\"em\") \" \" strong(\"strong\") \" \" strong(\"strong\") \" \" del(\"del\"))")]
    [InlineData("***both***", "p(em(strong(\"both\")))")]
    [InlineData("**bold *and em***", "p(strong(\"bold \" em(\"and em\")))")]
    [InlineData("*a **b** c*", "p(em(\"a \" strong(\"b\") \" c\"))")]
    [InlineData("foo*bar*baz", "p(\"foo\" em(\"bar\") \"baz\")")]
    [InlineData("~~**bold strike**~~", "p(del(strong(\"bold strike\")))")]
    [InlineData("**multi\nline**", "p(strong(\"multi\" br \"line\"))")]
    [InlineData("2 * 3 * 4", "p(\"2 * 3 * 4\")")]
    [InlineData("**foo*", "p(\"*\" em(\"foo\"))")]
    [InlineData("~single~ and ~5 minutes", "p(\"~single~ and ~5 minutes\")")]
    public void Emphasis_AndStrikethrough(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("snake_case_name and file_name_here.txt")]
    [InlineData("my_variable = other_variable_name")]
    [InlineData("call do_the_thing() then __private_thing")]
    [InlineData("2_000_000 rows")]
    [InlineData("path/to/some_file_name.cs")]
    [InlineData("http://localhost:5000/api/get_items")]
    public void IntrawordUnderscores_StayLiteral(string markdown) =>
        Assert.Equal("p(\"" + markdown + "\")", Dump(markdown));

    [Theory]
    [InlineData("https://example.com/some_path_with_underscores")]
    [InlineData("https://example.com/a_b/c_d?e_f=g_h")]
    public void UrlsWithUnderscores_StayLiteralLinks(string url) =>
        Assert.Equal("p(a[" + url + "](\"" + url + "\"))", Dump(url));

    [Theory]
    [InlineData("**bold", "p(\"**bold\")")]
    [InlineData("trailing **bold", "p(\"trailing **bold\")")]
    [InlineData("*em", "p(\"*em\")")]
    [InlineData("_em", "p(\"_em\")")]
    [InlineData("~~del", "p(\"~~del\")")]
    [InlineData("`code", "p(\"`code\")")]
    [InlineData("``code`", "p(\"``code`\")")]
    [InlineData("**bold *em", "p(\"**bold *em\")")]
    [InlineData("done **bold** then *open", "p(\"done \" strong(\"bold\") \" then *open\")")]
    [InlineData("[text", "p(\"[text\")")]
    [InlineData("[text]", "p(\"[text]\")")]
    [InlineData("[text](", "p(\"[text](\")")]
    [InlineData("[text](https://exa", "p(\"[text](https://exa\")")]
    [InlineData("![alt](https://example.com/i", "p(\"![alt](\" a[https://example.com/i](\"https://example.com/i\"))")]
    [InlineData("<https://example", "p(\"<https://example\")")]
    public void UnclosedInlineMarkers_RenderAsLiteralText(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("`a`", "p(code\"a\")")]
    [InlineData("``a`b``", "p(code\"a`b\")")]
    [InlineData("` `` `", "p(code\"``\")")]
    [InlineData("`a\nb`", "p(code\"a b\")")]
    [InlineData("`**not bold**` and `[not](a link)`", "p(code\"**not bold**\" \" and \" code\"[not](a link)\")")]
    public void InlineCode_SingleAndMultipleBackticks(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("\\*a\\* \\_b\\_ \\# \\[c\\] \\`d\\`", "p(\"*a* _b_ # [c] `d`\")")]
    [InlineData("\\a \\1", "p(\"\\a \\1\")")]
    [InlineData("\\\\*em*", "p(\"\\\" em(\"em\"))")]
    [InlineData("1\\. not a list", "p(\"1. not a list\")")]
    [InlineData("\\- not a list", "p(\"- not a list\")")]
    public void BackslashEscapes(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("[text](https://e.com)", "p(a[https://e.com](\"text\"))")]
    [InlineData("[t](https://e.com \"Title\")", "p(a[https://e.com|Title](\"t\"))")]
    [InlineData("[t](<https://e.com/a b>)", "p(a[https://e.com/a b](\"t\"))")]
    [InlineData("[w](https://en.wikipedia.org/wiki/Foo_(bar))", "p(a[https://en.wikipedia.org/wiki/Foo_(bar)](\"w\"))")]
    [InlineData("[**b** and `c`](https://e.com)", "p(a[https://e.com](strong(\"b\") \" and \" code\"c\"))")]
    [InlineData("[]()", "p(a[]())")]
    [InlineData("see [docs](https://e.com).", "p(\"see \" a[https://e.com](\"docs\") \".\")")]
    [InlineData("[ref]\n\n[ref]: https://e.com \"T\"", "p(a[https://e.com|T](\"ref\"))")]
    public void Links(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("![alt text](https://e.com/i.png \"T\")", "p(img[https://e.com/i.png|alt text|T])")]
    [InlineData("![a *b* `c`](https://e.com/i.png)", "p(img[https://e.com/i.png|a b c])")]
    [InlineData("![](https://e.com/i.png)", "p(img[https://e.com/i.png|])")]
    [InlineData("[![badge](https://e.com/b.svg)](https://e.com)", "p(a[https://e.com](img[https://e.com/b.svg|badge]))")]
    [InlineData("! not an image", "p(\"! not an image\")")]
    public void Images(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("<https://e.com/a?b=1>", "p(a[https://e.com/a?b=1](\"https://e.com/a?b=1\"))")]
    [InlineData("<user@example.com>", "p(a[mailto:user@example.com](\"user@example.com\"))")]
    [InlineData("<mailto:user@example.com>", "p(a[mailto:user@example.com](\"mailto:user@example.com\"))")]
    [InlineData("Visit https://e.com.", "p(\"Visit \" a[https://e.com](\"https://e.com\") \".\")")]
    [InlineData("(https://e.com)", "p(\"(\" a[https://e.com](\"https://e.com\") \")\")")]
    [InlineData(
        "See https://en.wikipedia.org/wiki/Foo_(bar)!",
        "p(\"See \" a[https://en.wikipedia.org/wiki/Foo_(bar)](\"https://en.wikipedia.org/wiki/Foo_(bar)\") \"!\")")]
    [InlineData("www.example.com/path", "p(a[http://www.example.com/path](\"www.example.com/path\"))")]
    public void Autolinks(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Theory]
    [InlineData("<b>bold</b>", "p(\"<b>bold</b>\")")]
    [InlineData("<div class=\"x\">\nhi\n</div>", "p(\"<div class=\"x\">\" br \"hi\" br \"</div>\")")]
    [InlineData("<!-- comment -->", "p(\"<!-- comment -->\")")]
    [InlineData("<script>alert(1)</script>", "p(\"<script>alert(1)</script>\")")]
    [InlineData("a < b > c", "p(\"a < b > c\")")]
    public void RawHtml_IsShownAsLiteralText(string markdown, string expected) =>
        Assert.Equal(expected, Dump(markdown));

    [Fact]
    public void Entities_AreDecoded() =>
        Assert.Equal("p(\"AT&T <tag> © #\")", Dump("AT&amp;T &lt;tag&gt; &copy; &#35;"));

    [Fact]
    public void Parse_NullOrEmpty_ReturnsNoNodes()
    {
        Assert.Empty(MarkdownRichTextParser.Parse(null!));
        Assert.Empty(MarkdownRichTextParser.Parse(string.Empty));
        Assert.Empty(MarkdownRichTextParser.Parse("   \n\n \t "));
    }

    [Fact]
    public void Parse_NulCharacters_AreReplaced() =>
        Assert.Equal("p(\"a\uFFFDb\")", Dump(MarkdownRichTextParser.Parse("a\0b")));

    [Fact]
    public void DeeplyNestedContainers_KeepTheirText()
    {
        // Within Markdig's nesting limit (128) the structure maps as is and the renderer bounds
        // the view depth; beyond the limit Markdig throws and Parse falls back to the plain text.
        string[] inputs =
        [
            new string('>', 100) + " deep",
            new string('>', 500) + " deep",
            string.Concat(Enumerable.Range(0, 60).Select(level => new string(' ', level * 2) + "- item" + level + "\n")),
            string.Concat(Enumerable.Range(0, 300).Select(level => new string(' ', level * 2) + "- item" + level + "\n")),
            string.Concat(Enumerable.Repeat("*a _b ", 40)) + string.Concat(Enumerable.Repeat(" c_ d*", 40)),
            string.Concat(Enumerable.Repeat("*a _b ", 2000)) + string.Concat(Enumerable.Repeat(" c_ d*", 2000)),
            string.Concat(Enumerable.Repeat("[a ", 500)) + "x" + string.Concat(Enumerable.Repeat("](https://e.com)", 500)),
        ];

        foreach (var markdown in inputs)
        {
            var nodes = MarkdownRichTextParser.Parse(markdown);
            Assert.True(IsSubsequence(LettersAndDigits(markdown), LettersAndDigits(GetVisibleText(nodes))));
        }
    }

    [Fact]
    public void InputBeyondMarkdigsNestingLimit_FallsBackToItsPlainText()
    {
        var markdown = new string('>', 200) + " deep\nsecond line";

        Assert.ThrowsAny<ArgumentException>(() => MarkdownRichTextParser.ParseWithoutFallback(markdown));
        Assert.Equal(
            "p(\"" + new string('>', 200) + " deep\" br \"second line\")",
            Dump(MarkdownRichTextParser.Parse(markdown)));
    }

    [Fact]
    public void RandomInput_NeverThrows()
    {
        const string alphabet = "ab1 \n\t*_~`[]()<>!#|-+:.\\\"'>xh/&;";
        var random = new Random(1234);
        for (var iteration = 0; iteration < 3000; iteration++)
        {
            var length = random.Next(0, 160);
            var characters = new char[length];
            for (var i = 0; i < length; i++)
                characters[i] = alphabet[random.Next(alphabet.Length)];

            Assert.NotNull(MarkdownRichTextParser.ParseWithoutFallback(new string(characters)));
        }
    }

    [Fact]
    public void PathologicalInput_ParsesWithoutThrowingOrLosingText()
    {
        string[] inputs =
        [
            new string('*', 50_000),
            new string('_', 50_000),
            new string('`', 50_000),
            new string('[', 50_000),
            new string(']', 50_000),
            new string('>', 50_000),
            new string('<', 50_000),
            new string('|', 50_000),
            string.Concat(Enumerable.Repeat("[a](", 12_000)),
            string.Concat(Enumerable.Repeat("[a](b \"", 8_000)),
            string.Concat(Enumerable.Repeat("<a:", 15_000)),
            string.Concat(Enumerable.Repeat("**a", 15_000)),
            string.Concat(Enumerable.Repeat("`a ``b ", 8_000)),
            string.Concat(Enumerable.Repeat("![[", 15_000)) + string.Concat(Enumerable.Repeat("[x](y)", 5_000)),
            "https://example.com/" + new string(')', 50_000),
            string.Concat(Enumerable.Repeat("- ", 20_000)),
            string.Concat(Enumerable.Repeat("| a ", 12_000)) + "\n" + string.Concat(Enumerable.Repeat("|---", 12_000)),
        ];

        foreach (var markdown in inputs)
        {
            var nodes = MarkdownRichTextParser.Parse(markdown);
            Assert.NotEmpty(nodes);
            Assert.True(IsSubsequence(LettersAndDigits(markdown), LettersAndDigits(GetVisibleText(nodes))));
        }
    }

    internal static string LettersAndDigits(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    internal static bool IsSubsequence(string needle, string haystack)
    {
        var index = 0;
        foreach (var character in haystack)
        {
            if (index < needle.Length && needle[index] == character)
                index++;
        }

        return index == needle.Length;
    }
}
