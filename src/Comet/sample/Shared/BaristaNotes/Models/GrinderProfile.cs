using System;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

/// <summary>
/// Per-grinder calibration profile. Holds scale bounds and anchor points
/// (micron ↔ setting) for deterministic translation.
/// </summary>
public class GrinderProfile
{
    public int Id { get; set; }
    public int EquipmentId { get; set; }
    public decimal? MinSetting { get; set; }
    public decimal? MaxSetting { get; set; }
    public decimal? StepSize { get; set; }

    /// <summary>
    /// JSON array of calibration anchors:
    /// { "micron": 250, "setting": 1.8, "source": "ai"|"user"|"community", "updatedAt": "..." }
    /// </summary>
    public string? AnchorsJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public Guid SyncId { get; set; }
    public bool IsDeleted { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    public Equipment? Equipment { get; set; }
}
