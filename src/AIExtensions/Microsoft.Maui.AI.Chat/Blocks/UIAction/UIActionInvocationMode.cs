// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>Controls when a registered UI action is invoked.</summary>
public enum UIActionInvocationMode
{
    /// <summary>Invokes the action when it is requested by the model.</summary>
    Automatic,

    /// <summary>Waits for the UI to invoke the action after supplying any required input.</summary>
    Manual,
}
