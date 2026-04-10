// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Go;

/// <summary>
/// Wire protocol messages between Comet Go dev server and companion app.
/// All messages are length-prefixed binary frames sent over WebSocket.
/// </summary>
public enum GoMessageType : byte
{
	/// <summary>Server → Client: Apply a hot reload delta.</summary>
	Delta = 1,

	/// <summary>Server → Client: Compilation error(s) in user code.</summary>
	CompilationError = 2,

	/// <summary>Client → Server: Handshake with device info.</summary>
	Hello = 3,

	/// <summary>Server → Client: Handshake acknowledgment with project info.</summary>
	Welcome = 4,

	/// <summary>Server → Client: Full project assembly (initial load).</summary>
	InitialAssembly = 5,

	/// <summary>Either direction: Keepalive ping.</summary>
	Ping = 6,

	/// <summary>Either direction: Keepalive pong.</summary>
	Pong = 7,

	/// <summary>Server → Client: Restart required (unsupported edit).</summary>
	RestartRequired = 8,
}
