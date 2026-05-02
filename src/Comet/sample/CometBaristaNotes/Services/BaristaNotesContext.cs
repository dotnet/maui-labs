namespace CometBaristaNotes.Services;

using Microsoft.EntityFrameworkCore;

public class BaristaNotesContext : DbContext
{
	public DbSet<ShotRecord> Shots => Set<ShotRecord>();
	public DbSet<Bean> Beans => Set<Bean>();
	public DbSet<Bag> Bags => Set<Bag>();
	public DbSet<Equipment> Equipment => Set<Equipment>();
	public DbSet<UserProfile> Profiles => Set<UserProfile>();

	private readonly string _dbPath;

	public BaristaNotesContext()
	{
		_dbPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, "baristanotes.db");
	}

	public BaristaNotesContext(string dbPath)
	{
		_dbPath = dbPath;
	}

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlite($"Data Source={_dbPath}");
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<ShotRecord>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).ValueGeneratedOnAdd();
			entity.Ignore(e => e.BagDisplayName);
			entity.Ignore(e => e.BeanName);
			entity.Ignore(e => e.MachineName);
			entity.Ignore(e => e.GrinderName);
			entity.Ignore(e => e.MadeByName);
			entity.Ignore(e => e.MadeForName);
		});

		modelBuilder.Entity<Bean>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).ValueGeneratedOnAdd();
			entity.Ignore(e => e.Bags);
		});

		modelBuilder.Entity<Bag>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).ValueGeneratedOnAdd();
			entity.Ignore(e => e.BeanName);
			entity.Ignore(e => e.ShotCount);
			entity.Ignore(e => e.AverageRating);
		});

		modelBuilder.Entity<Equipment>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).ValueGeneratedOnAdd();
		});

		modelBuilder.Entity<UserProfile>(entity =>
		{
			entity.HasKey(e => e.Id);
			entity.Property(e => e.Id).ValueGeneratedOnAdd();
		});
	}
}
