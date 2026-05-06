using CometBaristaNotes.Models;

namespace CometBaristaNotes.Services.DTOs;

/// <summary>
/// Parameters for logging a shot via voice command.
/// </summary>
public record LogShotParameters(
	double DoseGrams,
	double OutputGrams,
	int TimeSeconds,
	int? Rating = null,
	string? TastingNotes = null,
	int? PreinfusionSeconds = null,
	int? BagId = null,
	int? MachineId = null,
	int? GrinderId = null
);

/// <summary>
/// Parameters for adding a bean via voice command.
/// </summary>
public record AddBeanParameters(
	string Name,
	string? Roaster = null,
	string? Origin = null,
	string? TastingNotes = null
);

/// <summary>
/// Parameters for adding a bag via voice command.
/// </summary>
public record AddBagParameters(
	string BeanName,
	DateOnly? RoastDate = null
);

/// <summary>
/// Parameters for rating a shot via voice command.
/// </summary>
public record RateShotParameters(
	int Rating,
	string? ShotReference = null
);

/// <summary>
/// Parameters for adding equipment via voice command.
/// </summary>
public record AddEquipmentParameters(
	string Name,
	EquipmentType Type,
	string? Notes = null
);

/// <summary>
/// Parameters for adding a user profile via voice command.
/// </summary>
public record AddProfileParameters(
	string Name
);

/// <summary>
/// Parameters for navigation via voice command.
/// </summary>
public record NavigateParameters(
	string Destination
);

/// <summary>
/// Parameters for querying data via voice command.
/// </summary>
public record QueryParameters(
	string QueryType,
	string Period = "today",
	string? Filter = null
);
