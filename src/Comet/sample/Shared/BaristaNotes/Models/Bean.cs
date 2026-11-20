using System;
using System.Collections.Generic;

namespace CometBaristaNotes.Models;

public class Bean
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Roaster { get; set; }
    public string? Origin { get; set; }
    public string? Notes { get; set; }
    public string? RoasterUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Guid SyncId { get; set; }
    public DateTime LastModifiedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    [System.Text.Json.Serialization.JsonIgnore]
    public List<Bag> Bags { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public List<Recipe> Recipes { get; set; } = new();
}
