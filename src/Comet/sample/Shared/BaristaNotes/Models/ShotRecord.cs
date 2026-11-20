using System;
using System.Collections.Generic;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

public class ShotRecord
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }

    // Foreign keys
    public int BagId { get; set; }
    public int? MachineId { get; set; }
    public int? GrinderId { get; set; }
    public int? MadeById { get; set; }
    public int? MadeForId { get; set; }

    /// <summary>
    /// Brew method for this record. Defaults to Espresso for backwards
    /// compatibility with pre-v2 rows.
    /// </summary>
    public BrewMethod BrewMethod { get; set; } = BrewMethod.Espresso;

    /// <summary>
    /// Optional JSON blob of brew-method-specific parameters.
    /// </summary>
    public string? ParametersJson { get; set; }

    public decimal DoseIn { get; set; }

    /// <summary>
    /// Grind size in microns. Canonical, grinder-agnostic. Nullable.
    /// </summary>
    public int? GrindMicrons { get; set; }

    /// <summary>
    /// Brew water temperature in canonical Celsius. Nullable.
    /// </summary>
    public decimal? WaterTempC { get; set; }

    public decimal ExpectedTime { get; set; }
    public decimal ExpectedOutput { get; set; }
    public string DrinkType { get; set; } = string.Empty;

    // Actual results
    public decimal? ActualTime { get; set; }
    public decimal? ActualOutput { get; set; }
    public decimal? PreinfusionTime { get; set; }

    // Rating (0-4 scale)
    public int? Rating { get; set; }

    public string? TastingNotes { get; set; }

    // Sync metadata
    public Guid SyncId { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    // Navigation (populated by service layer, not EF)
    [System.Text.Json.Serialization.JsonIgnore]
    public Bag? Bag { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Equipment? Machine { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Equipment? Grinder { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public UserProfile? MadeBy { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public UserProfile? MadeFor { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ShotEquipment> ShotEquipments { get; set; } = new();
}
