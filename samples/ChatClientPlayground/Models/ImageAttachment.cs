namespace ChatClientPlayground.Models;

/// <summary>Contains an image copied into memory for a single chat message.</summary>
public sealed record ImageAttachment(string FileName, string MediaType, byte[] Bytes);
