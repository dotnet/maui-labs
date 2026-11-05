using System;

namespace CometBaristaNotes.Models;

public sealed class PendingAvatarCleanup
{
    public int ProfileId { get; set; }
    public string AvatarPath { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
