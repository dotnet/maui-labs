// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.Go;

/// <summary>
/// Binary wire format for Comet Go messages.
///
/// Frame layout:
///   [4 bytes: total payload length (big-endian)]
///   [1 byte:  message type]
///   [N bytes: message-specific payload]
///
/// Delta payload:
///   [4 bytes: sequence number]
///   [4 bytes: assembly name UTF-8 length] [N bytes: assembly name]
///   [4 bytes: metadata delta length]       [N bytes: metadata delta]
///   [4 bytes: IL delta length]             [N bytes: IL delta]
///   [4 bytes: PDB delta length]            [N bytes: PDB delta]
///
/// InitialAssembly payload:
///   [4 bytes: assembly name UTF-8 length] [N bytes: assembly name]
///   [4 bytes: PE length]                  [N bytes: PE bytes]
///   [4 bytes: PDB length]                 [N bytes: PDB bytes]
///
/// Hello payload:  JSON UTF-8 (HelloMessage)
/// Welcome payload: JSON UTF-8 (WelcomeMessage)
/// CompilationError payload: JSON UTF-8 (CompilationErrorMessage)
/// RestartRequired payload: JSON UTF-8 (RestartRequiredMessage)
/// Ping/Pong: empty payload
/// </summary>
public static class GoProtocol
{
	public const int HeaderSize = 5; // 4 bytes length + 1 byte type
	public const int DefaultPort = 9000;
	public const string DefaultPath = "/maui-go";

	/// <summary>
	/// Encode a delta update into a binary frame.
	/// </summary>
	public static byte[] EncodeDelta(DeltaPayload delta)
	{
		var nameBytes = Encoding.UTF8.GetBytes(delta.AssemblyName);
		var payloadLen = 4 + 4 + nameBytes.Length +
						 4 + delta.MetadataDelta.Length +
						 4 + delta.ILDelta.Length +
						 4 + delta.PdbDelta.Length;

		var frame = new byte[HeaderSize + payloadLen];
		var span = frame.AsSpan();

		// Header
		BinaryPrimitives.WriteInt32BigEndian(span, payloadLen + 1); // +1 for type byte
		span[4] = (byte)GoMessageType.Delta;

		var offset = HeaderSize;

		// Sequence
		BinaryPrimitives.WriteInt32BigEndian(span[offset..], delta.Sequence);
		offset += 4;

		// Assembly name
		BinaryPrimitives.WriteInt32BigEndian(span[offset..], nameBytes.Length);
		offset += 4;
		nameBytes.CopyTo(span[offset..]);
		offset += nameBytes.Length;

		// Metadata delta
		BinaryPrimitives.WriteInt32BigEndian(span[offset..], delta.MetadataDelta.Length);
		offset += 4;
		delta.MetadataDelta.CopyTo(span[offset..]);
		offset += delta.MetadataDelta.Length;

		// IL delta
		BinaryPrimitives.WriteInt32BigEndian(span[offset..], delta.ILDelta.Length);
		offset += 4;
		delta.ILDelta.CopyTo(span[offset..]);
		offset += delta.ILDelta.Length;

		// PDB delta
		BinaryPrimitives.WriteInt32BigEndian(span[offset..], delta.PdbDelta.Length);
		offset += 4;
		delta.PdbDelta.CopyTo(span[offset..]);

		return frame;
	}

	/// <summary>
	/// Decode a delta payload from binary data (after header is stripped).
	/// </summary>
	public static DeltaPayload DecodeDelta(ReadOnlySpan<byte> payload)
	{
		var offset = 0;

		var sequence = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;

		var nameLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var assemblyName = Encoding.UTF8.GetString(payload.Slice(offset, nameLen));
		offset += nameLen;

		var metaLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var metadataDelta = payload.Slice(offset, metaLen).ToArray();
		offset += metaLen;

		var ilLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var ilDelta = payload.Slice(offset, ilLen).ToArray();
		offset += ilLen;

		var pdbLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var pdbDelta = payload.Slice(offset, pdbLen).ToArray();

		return new DeltaPayload(sequence, assemblyName, metadataDelta, ilDelta, pdbDelta);
	}

	/// <summary>
	/// Encode an initial assembly load into a binary frame.
	/// </summary>
	public static byte[] EncodeInitialAssembly(string assemblyName, byte[] pe, byte[] pdb)
	{
		var nameBytes = Encoding.UTF8.GetBytes(assemblyName);
		var payloadLen = 4 + nameBytes.Length + 4 + pe.Length + 4 + pdb.Length;

		var frame = new byte[HeaderSize + payloadLen];
		var span = frame.AsSpan();

		BinaryPrimitives.WriteInt32BigEndian(span, payloadLen + 1);
		span[4] = (byte)GoMessageType.InitialAssembly;

		var offset = HeaderSize;

		BinaryPrimitives.WriteInt32BigEndian(span[offset..], nameBytes.Length);
		offset += 4;
		nameBytes.CopyTo(span[offset..]);
		offset += nameBytes.Length;

		BinaryPrimitives.WriteInt32BigEndian(span[offset..], pe.Length);
		offset += 4;
		pe.CopyTo(span[offset..]);
		offset += pe.Length;

		BinaryPrimitives.WriteInt32BigEndian(span[offset..], pdb.Length);
		offset += 4;
		pdb.CopyTo(span[offset..]);

		return frame;
	}

	/// <summary>
	/// Decode an initial assembly from binary data (after header is stripped).
	/// </summary>
	public static (string AssemblyName, byte[] Pe, byte[] Pdb) DecodeInitialAssembly(ReadOnlySpan<byte> payload)
	{
		var offset = 0;

		var nameLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var assemblyName = Encoding.UTF8.GetString(payload.Slice(offset, nameLen));
		offset += nameLen;

		var peLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var pe = payload.Slice(offset, peLen).ToArray();
		offset += peLen;

		var pdbLen = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
		offset += 4;
		var pdb = payload.Slice(offset, pdbLen).ToArray();

		return (assemblyName, pe, pdb);
	}

	/// <summary>
	/// Encode a JSON message (Hello, Welcome, CompilationError, RestartRequired).
	/// </summary>
	public static byte[] EncodeJson<T>(GoMessageType type, T message)
	{
		var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
		var frame = new byte[HeaderSize + json.Length];
		var span = frame.AsSpan();

		BinaryPrimitives.WriteInt32BigEndian(span, json.Length + 1);
		span[4] = (byte)type;
		json.CopyTo(span[HeaderSize..]);

		return frame;
	}

	/// <summary>
	/// Decode a JSON message from payload bytes.
	/// </summary>
	public static T DecodeJson<T>(ReadOnlySpan<byte> payload)
		=> JsonSerializer.Deserialize<T>(payload, JsonOptions)!;

	/// <summary>
	/// Encode a Ping or Pong frame (no payload).
	/// </summary>
	public static byte[] EncodePingPong(GoMessageType type)
	{
		var frame = new byte[HeaderSize];
		BinaryPrimitives.WriteInt32BigEndian(frame, 1); // just the type byte
		frame[4] = (byte)type;
		return frame;
	}

	/// <summary>
	/// Read the message type from a raw WebSocket binary frame.
	/// </summary>
	public static (GoMessageType Type, ReadOnlyMemory<byte> Payload) ParseFrame(ReadOnlyMemory<byte> frame)
	{
		var span = frame.Span;
		var totalLen = BinaryPrimitives.ReadInt32BigEndian(span);
		var type = (GoMessageType)span[4];
		var payload = frame.Slice(HeaderSize, totalLen - 1);
		return (type, payload);
	}

	static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
	};
}

/// <summary>
/// A hot reload delta for a single assembly.
/// </summary>
public sealed record DeltaPayload(
	int Sequence,
	string AssemblyName,
	byte[] MetadataDelta,
	byte[] ILDelta,
	byte[] PdbDelta);

/// <summary>
/// Client → Server handshake.
/// </summary>
public sealed class HelloMessage
{
	public string DeviceId { get; set; } = "";
	public string DeviceName { get; set; } = "";
	public string Platform { get; set; } = ""; // Android, iOS, MacCatalyst
	public string RuntimeVersion { get; set; } = "";
	public bool SupportsMetadataUpdate { get; set; }
}

/// <summary>
/// Server → Client handshake acknowledgment.
/// </summary>
public sealed class WelcomeMessage
{
	public string ProjectName { get; set; } = "";
	public string AssemblyName { get; set; } = "";
	public int ServerPort { get; set; }
}

/// <summary>
/// Server → Client compilation error report.
/// </summary>
public sealed class CompilationErrorMessage
{
	public List<CompilationDiagnostic> Errors { get; set; } = [];
}

public sealed class CompilationDiagnostic
{
	public string Id { get; set; } = "";
	public string Message { get; set; } = "";
	public string FilePath { get; set; } = "";
	public int Line { get; set; }
	public int Column { get; set; }
	public string Severity { get; set; } = "Error";
}

/// <summary>
/// Server → Client when an unsupported edit is detected.
/// </summary>
public sealed class RestartRequiredMessage
{
	public string Reason { get; set; } = "";
}
