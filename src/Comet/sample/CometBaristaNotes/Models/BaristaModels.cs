namespace CometBaristaNotes.Models;

public enum EquipmentType { Machine, Grinder, Tamper, PuckScreen, Other }

public enum ThemeMode { Light, Dark, Auto }

public class ShotRecord
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int BagId { get; set; }
    public int? MachineId { get; set; }
    public int? GrinderId { get; set; }
    public int? MadeById { get; set; }
    public int? MadeForId { get; set; }
    public decimal DoseIn { get; set; }
    public string GrindSetting { get; set; } = string.Empty;
    public decimal ExpectedTime { get; set; }
    public decimal ExpectedOutput { get; set; }
    public string DrinkType { get; set; } = "Espresso";
    public decimal? ActualTime { get; set; }
    public decimal? ActualOutput { get; set; }
    public decimal? PreinfusionTime { get; set; }
    public int? Rating { get; set; }
    public string? TastingNotes { get; set; }
    
    // Navigation helpers (populated by service)
    public string? BagDisplayName { get; set; }
    public string? BeanName { get; set; }
    public string? MachineName { get; set; }
    public string? GrinderName { get; set; }
    public string? MadeByName { get; set; }
    public string? MadeForName { get; set; }
}

public class Bean
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Roaster { get; set; }
    public string? Origin { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Bag> Bags { get; set; } = new();
}

public class Bag
{
    public int Id { get; set; }
    public int BeanId { get; set; }
    public string? BeanName { get; set; }
    public DateTime RoastDate { get; set; } = DateTime.Now;
    public string? Notes { get; set; }
    public bool IsComplete { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int ShotCount { get; set; }
    public double? AverageRating { get; set; }
}

public class Equipment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public EquipmentType Type { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class UserProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarPath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class RatingAggregate
{
    public double AverageRating { get; set; }
    public int TotalShots { get; set; }
    public int RatedShots { get; set; }
    public int? BestRating { get; set; }
    public int? WorstRating { get; set; }
    /// <summary>
    /// Rating level (0-4) → count of shots with that rating.
    /// </summary>
    public Dictionary<int, int> Distribution { get; set; } = new();
}
