using System;
using System.Collections.Generic;

using CometBaristaNotes.Models.Enums;

namespace CometBaristaNotes.Models;

public class Equipment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EquipmentType Type { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Guid SyncId { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<ShotEquipment> ShotEquipments { get; set; } = new();
}
