namespace CometBaristaNotes.Services;

public interface IDataStore : IShotService, IBeanService, IBagService, IEquipmentService, IUserProfileService, IRatingService
{
	IDataChangeNotifier? DataChangeNotifier { get; set; }
}

public interface IShotService
{
    List<ShotRecord> GetAllShots();
    ShotRecord? GetShot(int id);
    ShotRecord CreateShot(ShotRecord shot);
    ShotRecord UpdateShot(ShotRecord shot);
    void DeleteShot(int id);
    List<ShotRecord> GetShotsByBean(int beanId);
    List<ShotRecord> GetShotsForBag(int bagId);
    List<ShotRecord> GetFilteredShots(ShotFilterCriteria criteria);
    List<(int Id, string Name)> GetBeansWithShots();
    List<(int Id, string Name)> GetPeopleWithShots();
}

public interface IBeanService
{
    List<Bean> GetAllBeans();
    Bean? GetBean(int id);
    Bean CreateBean(Bean bean);
    Bean UpdateBean(Bean bean);
    void ArchiveBean(int id);
}

public interface IBagService
{
    List<Bag> GetAllBags();
    List<Bag> GetBagsForBean(int beanId);
    Bag? GetBag(int id);
    Bag CreateBag(Bag bag);
    Bag UpdateBag(Bag bag);
    void MarkComplete(int id);
    void ArchiveBag(int id);
    void ReactivateBag(int id);
}

public interface IEquipmentService
{
    List<Equipment> GetAllEquipment();
    List<Equipment> GetByType(EquipmentType type);
    Equipment? GetEquipment(int id);
    Equipment CreateEquipment(Equipment equipment);
    Equipment UpdateEquipment(Equipment equipment);
    void ArchiveEquipment(int id);
}

public interface IUserProfileService
{
    List<UserProfile> GetAllProfiles();
    UserProfile? GetProfile(int id);
    UserProfile CreateProfile(UserProfile profile);
    UserProfile UpdateProfile(UserProfile profile);
    void DeleteProfile(int id);
}

public interface IRatingService
{
    RatingAggregate GetBeanRating(int beanId);
    RatingAggregate GetBagRating(int bagId);
}
