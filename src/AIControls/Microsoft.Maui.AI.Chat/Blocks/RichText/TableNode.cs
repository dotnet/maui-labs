// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

public class TableNode : RichTextNode
{
    public IReadOnlyList<TableColumnAlignment> Alignment { get; set; } =
        Array.Empty<TableColumnAlignment>();
}
