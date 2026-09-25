namespace AIExtensions.Sample.ChatPlayground.Shared.Models;

/// <summary>Contains an image copied into memory for a chat request or image edit.</summary>
public sealed record ImageAttachment(string FileName, string MediaType, byte[] Bytes);
