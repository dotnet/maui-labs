namespace CometBaristaNotes.Services;

using Microsoft.EntityFrameworkCore;

public class SqliteDataStore : IDataStore
{
	public static SqliteDataStore Instance { get; private set; } = null!;
	public IDataChangeNotifier? DataChangeNotifier { get; set; }

	public SqliteDataStore()
	{
		Instance = this;
		Initialize();
	}

	private void Initialize()
	{
		using var db = CreateContext();
		db.Database.EnsureCreated();

		if (!db.Beans.Any())
		{
			SeedData(db);
		}
	}

	private BaristaNotesContext CreateContext() => new BaristaNotesContext();

	private void SeedData(BaristaNotesContext db)
	{
		// Beans
		var ethiopian = new Bean { Name = "Yirgacheffe", Roaster = "Counter Culture", Origin = "Ethiopia", Notes = "Bright, citrusy, floral" };
		var colombian = new Bean { Name = "Huila", Roaster = "Intelligentsia", Origin = "Colombia", Notes = "Chocolate, caramel, nutty" };
		var brazilian = new Bean { Name = "Cerrado", Roaster = "Onyx Coffee Lab", Origin = "Brazil", Notes = "Nutty, chocolate, low acidity" };
		var kenyan = new Bean { Name = "Nyeri AA", Roaster = "George Howell", Origin = "Kenya", Notes = "Blackcurrant, tomato, bright" };
		db.Beans.AddRange(ethiopian, colombian, brazilian, kenyan);
		db.SaveChanges();

		// Bags
		var bag1 = new Bag { BeanId = ethiopian.Id, RoastDate = DateTime.Now.AddDays(-14), Notes = "Fresh roast" };
		var bag2 = new Bag { BeanId = colombian.Id, RoastDate = DateTime.Now.AddDays(-7) };
		var bag3 = new Bag { BeanId = brazilian.Id, RoastDate = DateTime.Now.AddDays(-21), IsComplete = true };
		var bag4 = new Bag { BeanId = kenyan.Id, RoastDate = DateTime.Now.AddDays(-3) };
		db.Bags.AddRange(bag1, bag2, bag3, bag4);
		db.SaveChanges();

		// Equipment
		var machine1 = new Equipment { Name = "Linea Micra", Type = EquipmentType.Machine, Notes = "La Marzocco home machine" };
		var grinder1 = new Equipment { Name = "Niche Zero", Type = EquipmentType.Grinder, Notes = "Single dose conical burr" };
		var machine2 = new Equipment { Name = "Decent DE1", Type = EquipmentType.Machine, Notes = "Pressure profiling" };
		var tamper = new Equipment { Name = "Normcore V4", Type = EquipmentType.Tamper };
		var puckScreen = new Equipment { Name = "IMS Screen", Type = EquipmentType.PuckScreen };
		db.Equipment.AddRange(machine1, grinder1, machine2, tamper, puckScreen);
		db.SaveChanges();

		// Profiles
		var me = new UserProfile { Name = "Me" };
		var partner = new UserProfile { Name = "Partner" };
		db.Profiles.AddRange(me, partner);
		db.SaveChanges();

		// Shots
		db.Shots.AddRange(
			new ShotRecord
			{
				BagId = bag1.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = me.Id,
				DoseIn = 18m, GrindSetting = "15", ExpectedTime = 28, ExpectedOutput = 36,
				ActualTime = 27, ActualOutput = 35, Rating = 4, TastingNotes = "Bright and citrusy, nice body",
				DrinkType = "Espresso", Timestamp = DateTime.Now.AddHours(-2)
			},
			new ShotRecord
			{
				BagId = bag2.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = me.Id, MadeForId = partner.Id,
				DoseIn = 18m, GrindSetting = "14", ExpectedTime = 30, ExpectedOutput = 40,
				ActualTime = 32, ActualOutput = 42, Rating = 3, TastingNotes = "Slightly over-extracted, bitter finish",
				DrinkType = "Americano", Timestamp = DateTime.Now.AddHours(-5)
			},
			new ShotRecord
			{
				BagId = bag1.Id, MachineId = machine2.Id, GrinderId = grinder1.Id, MadeById = me.Id,
				DoseIn = 20m, GrindSetting = "16", ExpectedTime = 25, ExpectedOutput = 40,
				ActualTime = 24, ActualOutput = 38, Rating = 5, TastingNotes = "Perfect extraction, sweet and complex",
				DrinkType = "Espresso", Timestamp = DateTime.Now.AddDays(-1)
			},
			new ShotRecord
			{
				BagId = bag4.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = me.Id,
				DoseIn = 18m, GrindSetting = "13", ExpectedTime = 28, ExpectedOutput = 36,
				ActualTime = 22, ActualOutput = 30, Rating = 2, TastingNotes = "Under-extracted, sour",
				DrinkType = "Espresso", Timestamp = DateTime.Now.AddDays(-2)
			},
			new ShotRecord
			{
				BagId = bag3.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = partner.Id,
				DoseIn = 18m, GrindSetting = "15", ExpectedTime = 28, ExpectedOutput = 36,
				ActualTime = 29, ActualOutput = 37, Rating = 4,
				DrinkType = "Flat White", Timestamp = DateTime.Now.AddDays(-5)
			}
		);
		db.SaveChanges();
	}

	// SHOT SERVICE
	public List<ShotRecord> GetAllShots()
	{
		using var db = CreateContext();
		var shots = db.Shots.OrderByDescending(s => s.Timestamp).ToList();
		foreach (var shot in shots)
			PopulateShotNames(db, shot);
		return shots;
	}

	public ShotRecord? GetShot(int id)
	{
		using var db = CreateContext();
		var shot = db.Shots.FirstOrDefault(s => s.Id == id);
		if (shot != null)
			PopulateShotNames(db, shot);
		return shot;
	}

	public ShotRecord CreateShot(ShotRecord shot)
	{
		using var db = CreateContext();
		db.Shots.Add(shot);
		db.SaveChanges();
		PopulateShotNames(db, shot);
		DataChangeNotifier?.NotifyChange("Shot", shot.Id, DataChangeType.Created);
		return shot;
	}

	public ShotRecord UpdateShot(ShotRecord shot)
	{
		using var db = CreateContext();
		db.Shots.Update(shot);
		db.SaveChanges();
		PopulateShotNames(db, shot);
		DataChangeNotifier?.NotifyChange("Shot", shot.Id, DataChangeType.Updated);
		return shot;
	}

	public void DeleteShot(int id)
	{
		using var db = CreateContext();
		var shot = db.Shots.FirstOrDefault(s => s.Id == id);
		if (shot != null)
		{
			db.Shots.Remove(shot);
			db.SaveChanges();
		}
		DataChangeNotifier?.NotifyChange("Shot", id, DataChangeType.Deleted);
	}

	public List<ShotRecord> GetShotsByBean(int beanId)
	{
		using var db = CreateContext();
		var bagIds = db.Bags.Where(b => b.BeanId == beanId).Select(b => b.Id).ToHashSet();
		var shots = db.Shots.Where(s => bagIds.Contains(s.BagId)).OrderByDescending(s => s.Timestamp).ToList();
		foreach (var shot in shots)
			PopulateShotNames(db, shot);
		return shots;
	}

	public List<ShotRecord> GetShotsForBag(int bagId)
	{
		using var db = CreateContext();
		var shots = db.Shots.Where(s => s.BagId == bagId).OrderByDescending(s => s.Timestamp).ToList();
		foreach (var shot in shots)
			PopulateShotNames(db, shot);
		return shots;
	}

	public List<ShotRecord> GetFilteredShots(ShotFilterCriteria criteria)
	{
		using var db = CreateContext();
		var query = db.Shots.AsQueryable();

		if (criteria.BeanIds.Count > 0)
		{
			var bagIds = db.Bags.Where(b => criteria.BeanIds.Contains(b.BeanId)).Select(b => b.Id).ToHashSet();
			query = query.Where(s => bagIds.Contains(s.BagId));
		}

		if (criteria.MadeForIds.Count > 0)
			query = query.Where(s => s.MadeForId.HasValue && criteria.MadeForIds.Contains(s.MadeForId.Value));

		if (criteria.Ratings.Count > 0)
			query = query.Where(s => s.Rating.HasValue && criteria.Ratings.Contains(s.Rating.Value));

		var shots = query.OrderByDescending(s => s.Timestamp).ToList();
		foreach (var shot in shots)
			PopulateShotNames(db, shot);
		return shots;
	}

	public List<(int Id, string Name)> GetBeansWithShots()
	{
		using var db = CreateContext();
		var bagIdsWithShots = db.Shots.Select(s => s.BagId).Distinct().ToHashSet();
		var beanIds = db.Bags.Where(b => bagIdsWithShots.Contains(b.Id)).Select(b => b.BeanId).Distinct().ToHashSet();
		return db.Beans.Where(b => beanIds.Contains(b.Id)).Select(b => new { b.Id, b.Name }).ToList().Select(b => (b.Id, b.Name)).ToList();
	}

	public List<(int Id, string Name)> GetPeopleWithShots()
	{
		using var db = CreateContext();
		var profileIds = db.Shots.Where(s => s.MadeForId.HasValue).Select(s => s.MadeForId!.Value).Distinct().ToHashSet();
		return db.Profiles.Where(p => profileIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToList().Select(p => (p.Id, p.Name)).ToList();
	}

	// BEAN SERVICE
	public List<Bean> GetAllBeans()
	{
		using var db = CreateContext();
		return db.Beans.Where(b => b.IsActive).ToList();
	}

	public Bean? GetBean(int id)
	{
		using var db = CreateContext();
		return db.Beans.FirstOrDefault(b => b.Id == id);
	}

	public Bean CreateBean(Bean bean)
	{
		using var db = CreateContext();
		db.Beans.Add(bean);
		db.SaveChanges();
		DataChangeNotifier?.NotifyChange("Bean", bean.Id, DataChangeType.Created);
		return bean;
	}

	public Bean UpdateBean(Bean bean)
	{
		using var db = CreateContext();
		db.Beans.Update(bean);
		db.SaveChanges();
		return bean;
	}

	public void ArchiveBean(int id)
	{
		using var db = CreateContext();
		var b = db.Beans.FirstOrDefault(b => b.Id == id);
		if (b != null)
		{
			b.IsActive = false;
			db.SaveChanges();
		}
	}

	// BAG SERVICE
	public List<Bag> GetAllBags()
	{
		using var db = CreateContext();
		return db.Bags.Where(b => b.IsActive).ToList();
	}

	public List<Bag> GetBagsForBean(int beanId)
	{
		using var db = CreateContext();
		var bags = db.Bags.Where(b => b.BeanId == beanId && b.IsActive).ToList();
		foreach (var bag in bags)
		{
			bag.BeanName = db.Beans.FirstOrDefault(bn => bn.Id == bag.BeanId)?.Name;
			bag.ShotCount = db.Shots.Count(s => s.BagId == bag.Id);
			var ratings = db.Shots.Where(s => s.BagId == bag.Id && s.Rating != null).Select(s => s.Rating!.Value).ToList();
			bag.AverageRating = ratings.Any() ? ratings.Average() : null;
		}
		return bags;
	}

	public Bag? GetBag(int id)
	{
		using var db = CreateContext();
		var bag = db.Bags.FirstOrDefault(b => b.Id == id);
		if (bag != null)
		{
			bag.BeanName = db.Beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name;
			bag.ShotCount = db.Shots.Count(s => s.BagId == bag.Id);
		}
		return bag;
	}

	public Bag CreateBag(Bag bag)
	{
		using var db = CreateContext();
		bag.BeanName = db.Beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name;
		db.Bags.Add(bag);
		db.SaveChanges();
		return bag;
	}

	public Bag UpdateBag(Bag bag)
	{
		using var db = CreateContext();
		db.Bags.Update(bag);
		db.SaveChanges();
		return bag;
	}

	public void MarkComplete(int id)
	{
		using var db = CreateContext();
		var b = db.Bags.FirstOrDefault(b => b.Id == id);
		if (b != null)
		{
			b.IsComplete = true;
			db.SaveChanges();
		}
	}

	public void ArchiveBag(int id)
	{
		using var db = CreateContext();
		var b = db.Bags.FirstOrDefault(b => b.Id == id);
		if (b != null)
		{
			b.IsActive = false;
			db.SaveChanges();
		}
	}

	public void ReactivateBag(int id)
	{
		using var db = CreateContext();
		var b = db.Bags.FirstOrDefault(b => b.Id == id);
		if (b != null)
		{
			b.IsComplete = false;
			db.SaveChanges();
		}
	}

	// EQUIPMENT SERVICE
	public List<Equipment> GetAllEquipment()
	{
		using var db = CreateContext();
		return db.Equipment.Where(e => e.IsActive).ToList();
	}

	public List<Equipment> GetByType(EquipmentType type)
	{
		using var db = CreateContext();
		return db.Equipment.Where(e => e.IsActive && e.Type == type).ToList();
	}

	public Equipment? GetEquipment(int id)
	{
		using var db = CreateContext();
		return db.Equipment.FirstOrDefault(e => e.Id == id);
	}

	public Equipment CreateEquipment(Equipment equipment)
	{
		using var db = CreateContext();
		db.Equipment.Add(equipment);
		db.SaveChanges();
		return equipment;
	}

	public Equipment UpdateEquipment(Equipment equipment)
	{
		using var db = CreateContext();
		db.Equipment.Update(equipment);
		db.SaveChanges();
		return equipment;
	}

	public void ArchiveEquipment(int id)
	{
		using var db = CreateContext();
		var e = db.Equipment.FirstOrDefault(e => e.Id == id);
		if (e != null)
		{
			e.IsActive = false;
			db.SaveChanges();
		}
	}

	// PROFILE SERVICE
	public List<UserProfile> GetAllProfiles()
	{
		using var db = CreateContext();
		return db.Profiles.ToList();
	}

	public UserProfile? GetProfile(int id)
	{
		using var db = CreateContext();
		return db.Profiles.FirstOrDefault(p => p.Id == id);
	}

	public UserProfile CreateProfile(UserProfile profile)
	{
		using var db = CreateContext();
		db.Profiles.Add(profile);
		db.SaveChanges();
		return profile;
	}

	public UserProfile UpdateProfile(UserProfile profile)
	{
		using var db = CreateContext();
		db.Profiles.Update(profile);
		db.SaveChanges();
		return profile;
	}

	public void DeleteProfile(int id)
	{
		using var db = CreateContext();
		var p = db.Profiles.FirstOrDefault(p => p.Id == id);
		if (p != null)
		{
			db.Profiles.Remove(p);
			db.SaveChanges();
		}
	}

	// RATING SERVICE
	public RatingAggregate GetBeanRating(int beanId)
	{
		using var db = CreateContext();
		var bagIds = db.Bags.Where(b => b.BeanId == beanId).Select(b => b.Id).ToHashSet();
		var shots = db.Shots.Where(s => bagIds.Contains(s.BagId)).ToList();
		return BuildAggregate(shots);
	}

	public RatingAggregate GetBagRating(int bagId)
	{
		using var db = CreateContext();
		var shots = db.Shots.Where(s => s.BagId == bagId).ToList();
		return BuildAggregate(shots);
	}

	private RatingAggregate BuildAggregate(List<ShotRecord> shots)
	{
		var rated = shots.Where(s => s.Rating.HasValue).ToList();
		return new RatingAggregate
		{
			TotalShots = shots.Count,
			RatedShots = rated.Count,
			AverageRating = rated.Any() ? rated.Average(s => s.Rating!.Value) : 0,
			BestRating = rated.Any() ? rated.Max(s => s.Rating!.Value) : null,
			WorstRating = rated.Any() ? rated.Min(s => s.Rating!.Value) : null,
		};
	}

	private void PopulateShotNames(BaristaNotesContext db, ShotRecord shot)
	{
		var bag = db.Bags.FirstOrDefault(b => b.Id == shot.BagId);
		shot.BagDisplayName = bag != null ? $"{db.Beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name} ({bag.RoastDate:MMM d})" : null;
		shot.BeanName = bag != null ? db.Beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name : null;
		shot.MachineName = shot.MachineId.HasValue ? db.Equipment.FirstOrDefault(e => e.Id == shot.MachineId)?.Name : null;
		shot.GrinderName = shot.GrinderId.HasValue ? db.Equipment.FirstOrDefault(e => e.Id == shot.GrinderId)?.Name : null;
		shot.MadeByName = shot.MadeById.HasValue ? db.Profiles.FirstOrDefault(p => p.Id == shot.MadeById)?.Name : null;
		shot.MadeForName = shot.MadeForId.HasValue ? db.Profiles.FirstOrDefault(p => p.Id == shot.MadeForId)?.Name : null;
	}
}
