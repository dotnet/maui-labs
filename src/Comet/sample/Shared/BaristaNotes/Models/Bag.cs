using System;
using System.Collections.Generic;

namespace CometBaristaNotes.Models;

public class Bag
{
    public int Id { get; set; }
    public int BeanId { get; set; }
    public DateTime RoastDate { get; set; }
    public string? Notes { get; set; }
    public bool IsComplete { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Guid SyncId { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    public Bean? Bean { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ShotRecord> ShotRecords { get; set; } = new();
}
