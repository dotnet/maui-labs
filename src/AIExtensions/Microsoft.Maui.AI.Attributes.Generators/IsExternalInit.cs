// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Polyfill for `record` and `init` keyword support in netstandard2.0.
// This is a well-known pattern for source generators targeting netstandard2.0.

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

#endif
