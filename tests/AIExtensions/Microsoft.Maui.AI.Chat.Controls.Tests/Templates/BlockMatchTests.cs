namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class BlockMatchTests
{
    [Fact]
    public void Is_MatchesTheTypeAndDerivedTypes()
    {
        var match = BlockMatch.Is<RichContentBlock>();

        Assert.True(match.Matches(new RichContentBlock()));
        Assert.True(match.Matches(Blocks.Text("hi")));
        Assert.False(match.Matches(Blocks.ToolCall("search")));
    }

    [Fact]
    public void Is_MatchesAGeneratedToolBlockType()
    {
        var weather = new WeatherBlock { Call = new FunctionCallContent("1", "get_weather") };

        Assert.True(BlockMatch.Is<WeatherBlock>().Matches(weather));
        Assert.True(BlockMatch.Is<FunctionInvocationContentBlock>().Matches(weather));
        Assert.False(BlockMatch.Is<WeatherBlock>().Matches(Blocks.ToolCall("get_weather")));
    }

    [Fact]
    public void BlockTypeMatch_WithoutAType_MatchesNoBlock() =>
        Assert.False(new BlockTypeMatch().Matches(Blocks.Text("hi")));

    [Fact]
    public void BlockTypeMatch_RejectsATypeThatIsNotABlock() =>
        Assert.Throws<ArgumentException>(() => new BlockTypeMatch { Type = typeof(string) });

    [Fact]
    public void Tool_MatchesCallsAndActionsCaseInsensitively()
    {
        var match = BlockMatch.Tool("GET_WEATHER");

        Assert.True(match.Matches(Blocks.ToolCall("get_weather")));
        Assert.True(match.Matches(Blocks.ToolCall("get_weather", result: "sunny")));
        Assert.True(match.Matches(Blocks.UIAction("get_weather", UIActionInvocationMode.Manual)));
        Assert.False(match.Matches(Blocks.ToolCall("search")));
        Assert.False(match.Matches(Blocks.Text("get_weather")));
    }

    [Fact]
    public void Tool_DoesNotMatchAnApproval()
    {
        // An approval shows through a wrapper, around the view that this match picks for the call it asks about.
        var approval = Blocks.Approval("get_weather");

        Assert.False(BlockMatch.Tool("get_weather").Matches(approval));
        Assert.True(BlockMatch.Tool("get_weather").Matches(approval.InnerBlock));
    }

    [Fact]
    public void ToolMatch_WithoutAName_MatchesNoBlock() =>
        Assert.False(new ToolMatch().Matches(Blocks.ToolCall("search")));

    [Fact]
    public void Tool_WithoutAName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BlockMatch.Tool(null!));
        Assert.Throws<ArgumentException>(() => BlockMatch.Tool(string.Empty));
    }

    [Fact]
    public void Role_MatchesTheBlockRoleCaseInsensitively()
    {
        Assert.True(BlockMatch.Role(ChatRole.User).Matches(Blocks.Text("hi", ChatRole.User)));
        Assert.False(BlockMatch.Role(ChatRole.User).Matches(Blocks.Text("hi", ChatRole.Assistant)));
        Assert.True(new RoleMatch { Role = "ASSISTANT" }.Matches(Blocks.Text("hi", ChatRole.Assistant)));
    }

    [Fact]
    public void RoleMatch_WithoutARole_MatchesNoBlock() =>
        Assert.False(new RoleMatch().Matches(Blocks.Text("hi")));

    [Fact]
    public void All_MatchesWhenEveryConditionMatches()
    {
        var match = BlockMatch.All(BlockMatch.Is<FunctionInvocationContentBlock>(), BlockMatch.Tool("search"));

        Assert.True(match.Matches(Blocks.ToolCall("search")));
        Assert.False(match.Matches(Blocks.ToolCall("other")));
        Assert.False(match.Matches(Blocks.UIAction("search", UIActionInvocationMode.Manual)));
    }

    [Fact]
    public void Any_MatchesWhenOneConditionMatches()
    {
        var match = BlockMatch.Any(BlockMatch.Is<ReasoningContentBlock>(), BlockMatch.Tool("search"));

        Assert.True(match.Matches(Blocks.Reasoning("why")));
        Assert.True(match.Matches(Blocks.ToolCall("search")));
        Assert.False(match.Matches(Blocks.Text("hi")));
    }

    [Fact]
    public void AllAndAny_WithoutConditions_MatchEveryBlockAndNoBlock()
    {
        Assert.True(new AllMatch().Matches(Blocks.Text("hi")));
        Assert.False(new AnyMatch().Matches(Blocks.Text("hi")));
    }

    [Fact]
    public void AllAndAny_IgnoreNullConditions()
    {
        var all = new AllMatch { Conditions = { null!, BlockMatch.Tool("search") } };
        var any = new AnyMatch { Conditions = { null!, BlockMatch.Tool("search") } };

        Assert.True(all.Matches(Blocks.ToolCall("search")));
        Assert.False(all.Matches(Blocks.Text("hi")));
        Assert.True(any.Matches(Blocks.ToolCall("search")));
        Assert.False(any.Matches(Blocks.Text("hi")));
    }

    [Fact]
    public void Not_InvertsItsMatch()
    {
        var match = BlockMatch.Not(BlockMatch.Role(ChatRole.User));

        Assert.True(match.Matches(Blocks.Text("hi", ChatRole.Assistant)));
        Assert.False(match.Matches(Blocks.Text("hi", ChatRole.User)));
    }

    [Fact]
    public void NotMatch_WithoutAMatch_MatchesNoBlock() =>
        Assert.False(new NotMatch().Matches(Blocks.Text("hi")));

    [Fact]
    public void When_UsesThePredicate()
    {
        var failed = BlockMatch.When(block => block is FunctionInvocationContentBlock { Result.Exception: not null });

        Assert.True(failed.Matches(Blocks.ToolCall("search", exception: new InvalidOperationException("boom"))));
        Assert.False(failed.Matches(Blocks.ToolCall("search", result: "ok")));
    }

    [Fact]
    public void ACustomMatch_DecidesForItself()
    {
        var match = new FailedCallMatch();

        Assert.True(match.Matches(Blocks.ToolCall("search", exception: new InvalidOperationException("boom"))));
        Assert.False(match.Matches(Blocks.ToolCall("search", result: "ok")));
        Assert.False(match.Matches(Blocks.Text("hi")));
    }

    [Fact]
    public void Matches_Compose()
    {
        // A failed search call or a search UI action, unless the user's.
        var match = BlockMatch.All(
            BlockMatch.Tool("search"),
            BlockMatch.Any(
                BlockMatch.When(block => block is FunctionInvocationContentBlock { Result.Exception: not null }),
                BlockMatch.Is<UIActionBlock>()),
            BlockMatch.Not(BlockMatch.Role(ChatRole.User)));

        Assert.True(match.Matches(Blocks.ToolCall("search", exception: new InvalidOperationException())));
        Assert.True(match.Matches(Blocks.UIAction("search", UIActionInvocationMode.Manual)));
        Assert.False(match.Matches(Blocks.ToolCall("search", result: "ok")));
        Assert.False(match.Matches(Blocks.ToolCall("other", exception: new InvalidOperationException())));
    }

    [Fact]
    public void Factories_RejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => BlockMatch.All(null!));
        Assert.Throws<ArgumentNullException>(() => BlockMatch.Any(null!));
        Assert.Throws<ArgumentNullException>(() => BlockMatch.Not(null!));
        Assert.Throws<ArgumentNullException>(() => BlockMatch.When(null!));
    }

    [Fact]
    public void Matches_WithANullBlock_Throws()
    {
        BlockMatch[] matches =
        [
            BlockMatch.Is<TextContentBlock>(),
            BlockMatch.Tool("search"),
            BlockMatch.Role(ChatRole.User),
            BlockMatch.All(),
            BlockMatch.Any(),
            BlockMatch.Not(BlockMatch.Tool("search")),
            BlockMatch.When(static _ => true),
        ];

        Assert.All(matches, match => Assert.Throws<ArgumentNullException>(() => match.Matches(null!)));
    }

    [Fact]
    public void CompositeMatches_TakeTheirConditionsAsContent()
    {
        Assert.Equal(nameof(AllMatch.Conditions), ContentProperty<AllMatch>());
        Assert.Equal(nameof(AnyMatch.Conditions), ContentProperty<AnyMatch>());
        Assert.Equal(nameof(NotMatch.Match), ContentProperty<NotMatch>());
    }

    private sealed class FailedCallMatch : BlockMatch
    {
        public override bool Matches(ContentBlock block) => block is FunctionInvocationContentBlock { Result.Exception: not null };
    }

    internal static string? ContentProperty<T>() =>
        typeof(T).GetCustomAttributes(typeof(ContentPropertyAttribute), inherit: true)
            .Cast<ContentPropertyAttribute>()
            .SingleOrDefault()?.Name;
}
