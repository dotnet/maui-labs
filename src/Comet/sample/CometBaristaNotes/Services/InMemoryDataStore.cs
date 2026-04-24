namespace CometBaristaNotes.Services;

public class InMemoryDataStore : IDataStore
{
    public static IDataStore Instance { get; set; } = null!;
    public IDataChangeNotifier? DataChangeNotifier { get; set; }

    private int _nextShotId = 1;
    private int _nextBeanId = 1;
    private int _nextBagId = 1;
    private int _nextEquipmentId = 1;
    private int _nextProfileId = 1;
    
    private readonly List<ShotRecord> _shots = new();
    private readonly List<Bean> _beans = new();
    private readonly List<Bag> _bags = new();
    private readonly List<Equipment> _equipment = new();
    private readonly List<UserProfile> _profiles = new();

    public InMemoryDataStore()
    {
        Instance = this;
        SeedData();
    }

    private void SeedData()
    {
        // Beans
        var ethiopian = CreateBean(new Bean { Name = "Yirgacheffe", Roaster = "Counter Culture", Origin = "Ethiopia", Notes = "Bright, citrusy, floral" });
        var colombian = CreateBean(new Bean { Name = "Huila", Roaster = "Intelligentsia", Origin = "Colombia", Notes = "Chocolate, caramel, nutty" });
        var brazilian = CreateBean(new Bean { Name = "Cerrado", Roaster = "Onyx Coffee Lab", Origin = "Brazil", Notes = "Nutty, chocolate, low acidity" });
        var kenyan = CreateBean(new Bean { Name = "Nyeri AA", Roaster = "George Howell", Origin = "Kenya", Notes = "Blackcurrant, tomato, bright" });

        // Bags
        var bag1 = CreateBag(new Bag { BeanId = ethiopian.Id, RoastDate = DateTime.Now.AddDays(-14), Notes = "Fresh roast" });
        var bag2 = CreateBag(new Bag { BeanId = colombian.Id, RoastDate = DateTime.Now.AddDays(-7) });
        var bag3 = CreateBag(new Bag { BeanId = brazilian.Id, RoastDate = DateTime.Now.AddDays(-21), IsComplete = true });
        var bag4 = CreateBag(new Bag { BeanId = kenyan.Id, RoastDate = DateTime.Now.AddDays(-3) });

        // Equipment
        var machine1 = CreateEquipment(new Equipment { Name = "Linea Micra", Type = EquipmentType.Machine, Notes = "La Marzocco home machine" });
        var grinder1 = CreateEquipment(new Equipment { Name = "Niche Zero", Type = EquipmentType.Grinder, Notes = "Single dose conical burr" });
        var machine2 = CreateEquipment(new Equipment { Name = "Decent DE1", Type = EquipmentType.Machine, Notes = "Pressure profiling" });
        var tamper = CreateEquipment(new Equipment { Name = "Normcore V4", Type = EquipmentType.Tamper });
        CreateEquipment(new Equipment { Name = "IMS Screen", Type = EquipmentType.PuckScreen });

        // Profiles
        var me = CreateProfile(new UserProfile { Name = "Me" });
        var partner = CreateProfile(new UserProfile { Name = "Partner" });

        // Shots
        CreateShot(new ShotRecord { BagId = bag1.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = me.Id,
            DoseIn = 18m, GrindSetting = "15", ExpectedTime = 28, ExpectedOutput = 36,
            ActualTime = 27, ActualOutput = 35, Rating = 4, TastingNotes = "Bright and citrusy, nice body",
            DrinkType = "Espresso", Timestamp = DateTime.Now.AddHours(-2) });
        CreateShot(new ShotRecord { BagId = bag2.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = me.Id, MadeForId = partner.Id,
            DoseIn = 18m, GrindSetting = "14", ExpectedTime = 30, ExpectedOutput = 40,
            ActualTime = 32, ActualOutput = 42, Rating = 3, TastingNotes = "Slightly over-extracted, bitter finish",
            DrinkType = "Americano", Timestamp = DateTime.Now.AddHours(-5) });
        CreateShot(new ShotRecord { BagId = bag1.Id, MachineId = machine2.Id, GrinderId = grinder1.Id, MadeById = me.Id,
            DoseIn = 20m, GrindSetting = "16", ExpectedTime = 25, ExpectedOutput = 40,
            ActualTime = 24, ActualOutput = 38, Rating = 5, TastingNotes = "Perfect extraction, sweet and complex",
            DrinkType = "Espresso", Timestamp = DateTime.Now.AddDays(-1) });
        CreateShot(new ShotRecord { BagId = bag4.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = me.Id,
            DoseIn = 18m, GrindSetting = "13", ExpectedTime = 28, ExpectedOutput = 36,
            ActualTime = 22, ActualOutput = 30, Rating = 2, TastingNotes = "Under-extracted, sour",
            DrinkType = "Espresso", Timestamp = DateTime.Now.AddDays(-2) });
        CreateShot(new ShotRecord { BagId = bag3.Id, MachineId = machine1.Id, GrinderId = grinder1.Id, MadeById = partner.Id,
            DoseIn = 18m, GrindSetting = "15", ExpectedTime = 28, ExpectedOutput = 36,
            ActualTime = 29, ActualOutput = 37, Rating = 4,
            DrinkType = "Flat White", Timestamp = DateTime.Now.AddDays(-5) });
    }

    // SHOT SERVICE
    public List<ShotRecord> GetAllShots() => _shots.OrderByDescending(s => s.Timestamp).ToList();
    public ShotRecord? GetShot(int id) => _shots.FirstOrDefault(s => s.Id == id);
    public ShotRecord CreateShot(ShotRecord shot)
    {
        shot.Id = _nextShotId++;
        PopulateShotNames(shot);
        _shots.Add(shot);
        DataChangeNotifier?.NotifyChange("Shot", shot.Id, DataChangeType.Created);
        return shot;
    }
    public ShotRecord UpdateShot(ShotRecord shot)
    {
        var idx = _shots.FindIndex(s => s.Id == shot.Id);
        if (idx >= 0) { PopulateShotNames(shot); _shots[idx] = shot; }
        DataChangeNotifier?.NotifyChange("Shot", shot.Id, DataChangeType.Updated);
        return shot;
    }
    public void DeleteShot(int id)
    {
        _shots.RemoveAll(s => s.Id == id);
        DataChangeNotifier?.NotifyChange("Shot", id, DataChangeType.Deleted);
    }
    public List<ShotRecord> GetShotsByBean(int beanId)
    {
        var bagIds = _bags.Where(b => b.BeanId == beanId).Select(b => b.Id).ToHashSet();
        return _shots.Where(s => bagIds.Contains(s.BagId)).OrderByDescending(s => s.Timestamp).ToList();
    }

    public List<ShotRecord> GetShotsForBag(int bagId)
    {
        return _shots.Where(s => s.BagId == bagId).OrderByDescending(s => s.Timestamp).ToList();
    }

    public List<ShotRecord> GetFilteredShots(ShotFilterCriteria criteria)
    {
        var shots = _shots.AsEnumerable();

        if (criteria.BeanIds.Count > 0)
        {
            var bagIds = _bags.Where(b => criteria.BeanIds.Contains(b.BeanId)).Select(b => b.Id).ToHashSet();
            shots = shots.Where(s => bagIds.Contains(s.BagId));
        }

        if (criteria.MadeForIds.Count > 0)
            shots = shots.Where(s => s.MadeForId.HasValue && criteria.MadeForIds.Contains(s.MadeForId.Value));

        if (criteria.Ratings.Count > 0)
            shots = shots.Where(s => s.Rating.HasValue && criteria.Ratings.Contains(s.Rating.Value));

        return shots.OrderByDescending(s => s.Timestamp).ToList();
    }

    public List<(int Id, string Name)> GetBeansWithShots()
    {
        var bagIdsWithShots = _shots.Select(s => s.BagId).ToHashSet();
        var beanIds = _bags.Where(b => bagIdsWithShots.Contains(b.Id)).Select(b => b.BeanId).ToHashSet();
        return _beans.Where(b => beanIds.Contains(b.Id)).Select(b => (b.Id, b.Name)).ToList();
    }

    public List<(int Id, string Name)> GetPeopleWithShots()
    {
        var profileIds = _shots.Where(s => s.MadeForId.HasValue).Select(s => s.MadeForId!.Value).ToHashSet();
        return _profiles.Where(p => profileIds.Contains(p.Id)).Select(p => (p.Id, p.Name)).ToList();
    }

    // BEAN SERVICE
    public List<Bean> GetAllBeans() => _beans.Where(b => b.IsActive).ToList();
    public Bean? GetBean(int id) => _beans.FirstOrDefault(b => b.Id == id);
    public Bean CreateBean(Bean bean)
    {
        bean.Id = _nextBeanId++;
        _beans.Add(bean);
        DataChangeNotifier?.NotifyChange("Bean", bean.Id, DataChangeType.Created);
        return bean;
    }
    public Bean UpdateBean(Bean bean)
    {
        var idx = _beans.FindIndex(b => b.Id == bean.Id);
        if (idx >= 0) _beans[idx] = bean;
        return bean;
    }
    public void ArchiveBean(int id) { var b = GetBean(id); if (b != null) b.IsActive = false; }

    // BAG SERVICE
    public List<Bag> GetAllBags() => _bags.Where(b => b.IsActive).ToList();
    public List<Bag> GetBagsForBean(int beanId)
    {
        return _bags.Where(b => b.BeanId == beanId && b.IsActive).Select(b => {
            b.BeanName = _beans.FirstOrDefault(bn => bn.Id == b.BeanId)?.Name;
            b.ShotCount = _shots.Count(s => s.BagId == b.Id);
            var ratings = _shots.Where(s => s.BagId == b.Id && s.Rating.HasValue).Select(s => s.Rating!.Value);
            b.AverageRating = ratings.Any() ? ratings.Average() : null;
            return b;
        }).ToList();
    }
    public Bag? GetBag(int id)
    {
        var bag = _bags.FirstOrDefault(b => b.Id == id);
        if (bag != null) {
            bag.BeanName = _beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name;
            bag.ShotCount = _shots.Count(s => s.BagId == bag.Id);
        }
        return bag;
    }
    public Bag CreateBag(Bag bag)
    {
        bag.Id = _nextBagId++;
        bag.BeanName = _beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name;
        _bags.Add(bag);
        return bag;
    }
    public Bag UpdateBag(Bag bag)
    {
        var idx = _bags.FindIndex(b => b.Id == bag.Id);
        if (idx >= 0) _bags[idx] = bag;
        return bag;
    }
    public void MarkComplete(int id) { var b = GetBag(id); if (b != null) b.IsComplete = true; }
    public void ArchiveBag(int id) { var b = GetBag(id); if (b != null) b.IsActive = false; }
    public void ReactivateBag(int id) { var b = GetBag(id); if (b != null) b.IsComplete = false; }

    // EQUIPMENT SERVICE
    public List<Equipment> GetAllEquipment() => _equipment.Where(e => e.IsActive).ToList();
    public List<Equipment> GetByType(EquipmentType type) => _equipment.Where(e => e.IsActive && e.Type == type).ToList();
    public Equipment? GetEquipment(int id) => _equipment.FirstOrDefault(e => e.Id == id);
    public Equipment CreateEquipment(Equipment equipment) { equipment.Id = _nextEquipmentId++; _equipment.Add(equipment); return equipment; }
    public Equipment UpdateEquipment(Equipment equipment)
    {
        var idx = _equipment.FindIndex(e => e.Id == equipment.Id);
        if (idx >= 0) _equipment[idx] = equipment;
        return equipment;
    }
    public void ArchiveEquipment(int id) { var e = GetEquipment(id); if (e != null) e.IsActive = false; }

    // PROFILE SERVICE
    public List<UserProfile> GetAllProfiles() => _profiles.ToList();
    public UserProfile? GetProfile(int id) => _profiles.FirstOrDefault(p => p.Id == id);
    public UserProfile CreateProfile(UserProfile profile) { profile.Id = _nextProfileId++; _profiles.Add(profile); return profile; }
    public UserProfile UpdateProfile(UserProfile profile)
    {
        var idx = _profiles.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) _profiles[idx] = profile;
        return profile;
    }
    public void DeleteProfile(int id) => _profiles.RemoveAll(p => p.Id == id);

    // RATING SERVICE
    public RatingAggregate GetBeanRating(int beanId)
    {
        var bagIds = _bags.Where(b => b.BeanId == beanId).Select(b => b.Id).ToHashSet();
        var shots = _shots.Where(s => bagIds.Contains(s.BagId)).ToList();
        return BuildAggregate(shots);
    }
    public RatingAggregate GetBagRating(int bagId)
    {
        var shots = _shots.Where(s => s.BagId == bagId).ToList();
        return BuildAggregate(shots);
    }

    private RatingAggregate BuildAggregate(List<ShotRecord> shots)
    {
        var rated = shots.Where(s => s.Rating.HasValue).ToList();
        var dist = new Dictionary<int, int>();
        for (int i = 0; i <= 4; i++)
            dist[i] = rated.Count(s => s.Rating!.Value == i);
        return new RatingAggregate
        {
            TotalShots = shots.Count,
            RatedShots = rated.Count,
            AverageRating = rated.Any() ? rated.Average(s => s.Rating!.Value) : 0,
            BestRating = rated.Any() ? rated.Max(s => s.Rating!.Value) : null,
            WorstRating = rated.Any() ? rated.Min(s => s.Rating!.Value) : null,
            Distribution = dist,
        };
    }

    private void PopulateShotNames(ShotRecord shot)
    {
        var bag = _bags.FirstOrDefault(b => b.Id == shot.BagId);
        shot.BagDisplayName = bag != null ? $"{_beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name} ({bag.RoastDate:MMM d})" : null;
        shot.BeanName = bag != null ? _beans.FirstOrDefault(b => b.Id == bag.BeanId)?.Name : null;
        shot.MachineName = shot.MachineId.HasValue ? _equipment.FirstOrDefault(e => e.Id == shot.MachineId)?.Name : null;
        shot.GrinderName = shot.GrinderId.HasValue ? _equipment.FirstOrDefault(e => e.Id == shot.GrinderId)?.Name : null;
        shot.MadeByName = shot.MadeById.HasValue ? _profiles.FirstOrDefault(p => p.Id == shot.MadeById)?.Name : null;
        shot.MadeForName = shot.MadeForId.HasValue ? _profiles.FirstOrDefault(p => p.Id == shot.MadeForId)?.Name : null;
    }
}
