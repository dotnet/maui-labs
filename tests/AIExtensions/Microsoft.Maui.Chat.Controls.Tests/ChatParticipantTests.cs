using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers <see cref="ChatParticipant"/>: identity, validation, and derived presentation data.</summary>
public class ChatParticipantTests
{
    [Fact]
    public void Constructor_WithoutDisplayName_UsesId()
    {
        var participant = new ChatParticipant("u1");

        Assert.Equal("u1", participant.Id);
        Assert.Equal("u1", participant.DisplayName);
        Assert.Equal(ChatParticipantKind.Remote, participant.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankId_Throws(string? id) =>
        Assert.ThrowsAny<ArgumentException>(() => new ChatParticipant(id!));

    [Fact]
    public void Kind_Local_IsLocal()
    {
        var participant = ChatFactory.Local();

        Assert.True(participant.IsLocal);
    }

    [Fact]
    public void Kind_Change_RaisesIsLocal()
    {
        var participant = new ChatParticipant("u1");
        using var recorder = new PropertyRecorder(participant);

        participant.Kind = ChatParticipantKind.Local;

        Assert.Contains(nameof(ChatParticipant.IsLocal), recorder.Names);
        Assert.True(participant.IsLocal);
    }

    [Fact]
    public void DisplayName_IsBindableAndNotifies()
    {
        var participant = new ChatParticipant("u1", "Ada Lovelace");
        using var recorder = new PropertyRecorder(participant);

        participant.DisplayName = "Grace Hopper";

        Assert.Equal("Grace Hopper", participant.DisplayName);
        Assert.Contains(nameof(ChatParticipant.DisplayName), recorder.Names);
        Assert.Contains(nameof(ChatParticipant.Initials), recorder.Names);
    }

    [Fact]
    public void DisplayName_SetToNull_CoercesToEmpty()
    {
        var participant = new ChatParticipant("u1", "Ada");

        participant.DisplayName = null!;

        Assert.Equal(string.Empty, participant.DisplayName);
        Assert.Equal("?", participant.Initials);
    }

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("ada", "A")]
    [InlineData("  spaced   out  ", "SO")]
    [InlineData("one two three", "OT")]
    [InlineData("", "?")]
    [InlineData("***", "?")]
    public void Initials_AreDerivedFromDisplayName(string displayName, string expected)
    {
        var participant = new ChatParticipant("u1", displayName);

        Assert.Equal(expected, participant.Initials);
    }

    [Fact]
    public void Avatar_DefaultsToNull()
    {
        var participant = new ChatParticipant("u1");

        Assert.Null(participant.Avatar);
    }
}
