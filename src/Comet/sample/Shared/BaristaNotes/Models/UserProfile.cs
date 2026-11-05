using System;
using System.Collections.Generic;

namespace CometBaristaNotes.Models;

public class UserProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarPath { get; set; }

    /// <summary>
    /// Free-form learned context about this person (preferences, history insights, notes).
    /// Soft cap 2000 chars enforced in service.
    /// </summary>
    public string? Context { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid SyncId { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<ShotRecord> ShotsMadeBy { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ShotRecord> ShotsMadeFor { get; set; } = new();
}
