// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.AspNetCore.Components;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>
/// Verifies the registration side-effects of <see cref="MessageContentRenderer{TContent}"/>.
/// We drive the component via the <see cref="IComponent"/> protocol against a never-attached
/// render handle: the component no longer requires a live handle to publish its registration.
/// </summary>
public class MessageContentRendererTests
{
    [Fact]
    public async Task Sets_Registration_On_Initialize()
    {
        var context = CreateContext();
        var component = new MessageContentRenderer<TextMessageContent> { ViewContext = context };

        await SetParametersAsync(component, new()
        {
            [nameof(MessageContentRenderer<TextMessageContent>.ChildContent)] = (RenderFragment<TextMessageContent>)(_ => _ => { }),
        });

        Assert.Single(context.Registrations);
        Assert.Equal(typeof(TextMessageContent), context.Registrations[0].ContentType);
    }

    [Fact]
    public async Task Missing_ViewContext_Throws()
    {
        var component = new MessageContentRenderer<TextMessageContent>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SetParametersAsync(component, new()
            {
                [nameof(MessageContentRenderer<TextMessageContent>.ChildContent)] = (RenderFragment<TextMessageContent>)(_ => _ => { }),
            }));
    }

    [Fact]
    public async Task Missing_ChildContent_Throws()
    {
        var context = CreateContext();
        var component = new MessageContentRenderer<TextMessageContent> { ViewContext = context };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SetParametersAsync(component, new()));
    }

    [Fact]
    public async Task Dispose_RemovesRegistration()
    {
        var context = CreateContext();
        var component = new MessageContentRenderer<TextMessageContent> { ViewContext = context };

        await SetParametersAsync(component, new()
        {
            [nameof(MessageContentRenderer<TextMessageContent>.ChildContent)] = (RenderFragment<TextMessageContent>)(_ => _ => { }),
        });

        component.Dispose();

        Assert.Empty(context.Registrations);
    }

    [Fact]
    public async Task Registration_MatchesOnlyDeclaredType()
    {
        var context = CreateContext();
        var component = new MessageContentRenderer<MediaMessageContent> { ViewContext = context };

        await SetParametersAsync(component, new()
        {
            [nameof(MessageContentRenderer<MediaMessageContent>.ChildContent)] = (RenderFragment<MediaMessageContent>)(_ => _ => { }),
        });

        var textMatch = context.Resolve(new TextMessageContent("t"));
        var mediaMatch = context.Resolve(new MediaMessageContent(new ReadOnlyMemory<byte>(new byte[] { 1 }), "image/png"));

        Assert.Null(textMatch);
        Assert.NotNull(mediaMatch);
    }

    [Fact]
    public async Task WhenPredicate_NarrowsMatches()
    {
        var context = CreateContext();
        var component = new MessageContentRenderer<TextMessageContent> { ViewContext = context };

        await SetParametersAsync(component, new()
        {
            [nameof(MessageContentRenderer<TextMessageContent>.When)] = (Func<TextMessageContent, bool>)(c => c.Text.StartsWith("!")),
            [nameof(MessageContentRenderer<TextMessageContent>.ChildContent)] = (RenderFragment<TextMessageContent>)(_ => _ => { }),
        });

        Assert.Null(context.Resolve(new TextMessageContent("plain")));
        Assert.NotNull(context.Resolve(new TextMessageContent("!special")));
    }

    private static ChatViewContext CreateContext()
    {
        var composer = new ChatComposerContext();
        return new ChatViewContext(conversation: null, composer);
    }

    private static Task SetParametersAsync<TContent>(
        MessageContentRenderer<TContent> component,
        Dictionary<string, object?> parameters)
        where TContent : MessageContent =>
        ((IComponent)component).SetParametersAsync(ParameterView.FromDictionary(parameters));
}
