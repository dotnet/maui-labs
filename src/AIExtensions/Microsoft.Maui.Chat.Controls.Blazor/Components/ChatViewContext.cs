// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// The state cascaded from a <see cref="ChatView"/> to its message rows and any
/// <see cref="MessageContentRenderer{TContent}"/> registrations underneath it.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ChatViewContext"/> is created once per <see cref="Conversation"/>. Blazor's
/// cascade sits inside a region keyed on the context so descendants tear down and rebuild
/// when the conversation changes — the same trick <c>EditForm</c> uses with
/// <c>EditContext</c>, which lets the cascade stay <c>IsFixed=true</c>.
/// </para>
/// <para>
/// Registrations are appended in the order a <see cref="MessageContentRenderer{TContent}"/>
/// component is initialised, and the most recently added registration wins when several
/// match the same content — layer 2's AI-specific renderers therefore always sit above
/// the neutral defaults.
/// </para>
/// <para>Single-thread affine; add and remove registrations only on the renderer's UI thread.</para>
/// </remarks>
public sealed class ChatViewContext
{
    private readonly List<ChatContentRegistration> _registrations = new();
    private Action? _registrationsChanged;

    internal ChatViewContext(ChatConversation? conversation, IChatComposerContext composerContext)
    {
        Conversation = conversation;
        ComposerContext = composerContext;
    }

    /// <summary>Gets the conversation the view is bound to.</summary>
    public ChatConversation? Conversation { get; }

    /// <summary>Gets the participant that represents this device, when the conversation exposes one.</summary>
    public ChatParticipant? LocalParticipant => Conversation?.LocalParticipant;

    /// <summary>Gets the composer state a custom composer would bind to.</summary>
    public IChatComposerContext ComposerContext { get; }

    /// <summary>
    /// Gets a snapshot of the current registrations. The list is copied when a resolver reads
    /// it so the shell is safe against a renderer registering or disposing during iteration.
    /// </summary>
    public IReadOnlyList<ChatContentRegistration> Registrations => _registrations.ToArray();

    /// <summary>Adds a content renderer registration. Called by <see cref="MessageContentRenderer{TContent}"/>.</summary>
    /// <param name="registration">The registration to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registration"/> is <see langword="null"/>.</exception>
    public void AddRegistration(ChatContentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _registrations.Add(registration);
        _registrationsChanged?.Invoke();
    }

    /// <summary>Removes a content renderer registration. Called when a renderer is disposed.</summary>
    /// <param name="registration">The registration to remove.</param>
    /// <returns><see langword="true"/> if the registration was present.</returns>
    public bool RemoveRegistration(ChatContentRegistration registration)
    {
        var removed = _registrations.Remove(registration);
        if (removed)
        {
            _registrationsChanged?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// Resolves the highest-priority registration that matches <paramref name="content"/>. Most
    /// recently added registrations win, matching upstream <c>BlockRenderer</c> semantics.
    /// </summary>
    /// <param name="content">The content to render.</param>
    /// <returns>The matching registration, or <see langword="null"/> when the shell should fall back to its defaults.</returns>
    public ChatContentRegistration? Resolve(MessageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        for (var i = _registrations.Count - 1; i >= 0; i--)
        {
            var registration = _registrations[i];
            if (registration.ContentType.IsAssignableFrom(content.GetType())
                && registration.When(content))
            {
                return registration;
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a callback the shell fires when the registration set changed, so it can
    /// re-render rows whose fallback picks might now be overridden.
    /// </summary>
    /// <param name="onChanged">The callback to fire. <see langword="null"/> to clear.</param>
    internal void SetRegistrationsChangedHandler(Action? onChanged) =>
        _registrationsChanged = onChanged;
}
