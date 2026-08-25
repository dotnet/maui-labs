// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Exercises <see cref="ChatViewContext"/> registration lifecycle, matching order, and
/// most-recently-registered-wins semantics that the AI bridge layer will rely on.
/// </summary>
public class ChatViewContextTests
{
    private static ChatViewContext CreateContext()
    {
        var composer = new ChatComposerContext(
            EventCallback.Empty,
            EventCallback.Empty,
            EventCallback.Empty,
            EventCallback.Empty,
            EventCallback.Empty);
        return new ChatViewContext(conversation: null, composer);
    }

    [Fact]
    public void Resolve_NoRegistrations_ReturnsNull()
    {
        var context = CreateContext();

        var match = context.Resolve(new TextMessageContent("hi"));

        Assert.Null(match);
    }

    [Fact]
    public void AddRegistration_Then_Resolve_ReturnsIt()
    {
        var context = CreateContext();
        var registration = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };

        context.AddRegistration(registration);
        var match = context.Resolve(new TextMessageContent("hi"));

        Assert.Same(registration, match);
    }

    [Fact]
    public void Resolve_MostRecentlyAddedMatch_Wins()
    {
        var context = CreateContext();
        var first = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };
        var second = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };

        context.AddRegistration(first);
        context.AddRegistration(second);
        var match = context.Resolve(new TextMessageContent("hi"));

        Assert.Same(second, match);
    }

    [Fact]
    public void Resolve_HonorsWhenPredicate()
    {
        var context = CreateContext();
        var narrow = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = c => ((TextMessageContent)c).Text == "match",
            Render = _ => _ => { },
        };
        var broad = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };
        context.AddRegistration(broad);
        context.AddRegistration(narrow);

        var noMatch = context.Resolve(new TextMessageContent("nope"));
        var narrowMatch = context.Resolve(new TextMessageContent("match"));

        Assert.Same(broad, noMatch);
        Assert.Same(narrow, narrowMatch);
    }

    [Fact]
    public void Resolve_UsesBaseTypeAssignability()
    {
        var context = CreateContext();
        var registration = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };
        context.AddRegistration(registration);

        var structured = new StructuredTextMessageContent<string>("text", "doc");
        var match = context.Resolve(structured);

        Assert.Same(registration, match);
    }

    [Fact]
    public void RemoveRegistration_UnregistersIt()
    {
        var context = CreateContext();
        var registration = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };
        context.AddRegistration(registration);

        var removed = context.RemoveRegistration(registration);
        var match = context.Resolve(new TextMessageContent("hi"));

        Assert.True(removed);
        Assert.Null(match);
    }

    [Fact]
    public void Registrations_ReturnsSnapshot_NotBackingStore()
    {
        var context = CreateContext();
        var registration = new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        };
        context.AddRegistration(registration);

        var snapshot = context.Registrations;
        context.AddRegistration(new ChatContentRegistration
        {
            ContentType = typeof(TextMessageContent),
            When = _ => true,
            Render = _ => _ => { },
        });

        Assert.Single(snapshot);
    }

    [Fact]
    public void AddRegistration_Null_Throws()
    {
        var context = CreateContext();

        Assert.Throws<ArgumentNullException>(() => context.AddRegistration(null!));
    }
}
