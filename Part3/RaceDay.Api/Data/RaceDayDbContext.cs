using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Models;

namespace RaceDay.Api.Data;

public class RaceDayDbContext : DbContext
{
    public RaceDayDbContext(DbContextOptions<RaceDayDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<RaceRoute> Routes => Set<RaceRoute>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Result> Results => Set<Result>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users", t => t.HasCheckConstraint("CK_Users_Role",
                "[Role] IN (N'Participant', N'Organiser', N'Admin')"));
            e.HasKey(x => x.UserID).HasName("PK_Users");
            e.Property(x => x.FullName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.Role).HasMaxLength(20).IsRequired().HasDefaultValue(Roles.Participant);
            e.Property(x => x.DateCreated).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("UQ_Users_Email");
        });

        modelBuilder.Entity<Organisation>(e =>
        {
            e.ToTable("Organisations");
            e.HasKey(x => x.OrganisationID).HasName("PK_Organisations");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.ContactEmail).HasMaxLength(150).IsRequired();
            e.Property(x => x.ContactPhone).HasMaxLength(20);
            e.Property(x => x.DateCreated).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => x.ContactEmail).IsUnique().HasDatabaseName("UQ_Organisations_ContactEmail");
            e.HasOne(x => x.ManagedBy).WithMany(u => u.Organisations)
                .HasForeignKey(x => x.ManagedByUserID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Organisations_Users");
        });

        modelBuilder.Entity<Venue>(e =>
        {
            e.ToTable("Venues");
            e.HasKey(x => x.VenueID).HasName("PK_Venues");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Address).HasMaxLength(255);
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.Province).HasMaxLength(100).IsRequired();
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.ToTable("Events", t => t.HasCheckConstraint("CK_Events_Discipline",
                "[Discipline] IN (N'Running', N'Cycling', N'Walking')"));
            e.HasKey(x => x.EventID).HasName("PK_Events");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Discipline).HasMaxLength(20).IsRequired();
            e.Property(x => x.DateCreated).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasOne(x => x.Organisation).WithMany(o => o.Events)
                .HasForeignKey(x => x.OrganisationID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Events_Organisations");
            e.HasOne(x => x.Venue).WithMany(v => v.Events)
                .HasForeignKey(x => x.VenueID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Events_Venues");
        });

        modelBuilder.Entity<RaceRoute>(e =>
        {
            e.ToTable("Routes", t => t.HasCheckConstraint("CK_Routes_DistanceKM", "[DistanceKM] > 0"));
            e.HasKey(x => x.RouteID).HasName("PK_Routes");
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.DistanceKM).HasPrecision(6, 2);
            e.Property(x => x.ElevationGain).HasPrecision(7, 2).HasDefaultValue(0m);
            e.Property(x => x.SurfaceType).HasMaxLength(50);
            e.Property(x => x.GPXFileURL).HasMaxLength(500);
            e.HasOne(x => x.Event).WithMany(ev => ev.Routes)
                .HasForeignKey(x => x.EventID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Routes_Events");
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Categories", t =>
            {
                t.HasCheckConstraint("CK_Categories_Gender", "[Gender] IN (N'Male', N'Female', N'Open')");
                t.HasCheckConstraint("CK_Categories_EntryFee", "[EntryFee] >= 0");
                t.HasCheckConstraint("CK_Categories_AgeRange", "[MaxAge] >= [MinAge]");
            });
            e.HasKey(x => x.CategoryID).HasName("PK_Categories");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.DistanceKM).HasPrecision(6, 2);
            e.Property(x => x.MinAge).HasDefaultValue(0);
            e.Property(x => x.MaxAge).HasDefaultValue(99);
            e.Property(x => x.Gender).HasMaxLength(10).IsRequired().HasDefaultValue("Open");
            e.Property(x => x.EntryFee).HasPrecision(8, 2).HasDefaultValue(0m);
            e.HasOne(x => x.Event).WithMany(ev => ev.Categories)
                .HasForeignKey(x => x.EventID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Categories_Events");
            e.HasOne(x => x.Route).WithMany(r => r.Categories)
                .HasForeignKey(x => x.RouteID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Categories_Routes");
        });

        modelBuilder.Entity<Registration>(e =>
        {
            e.ToTable("Registrations", t => t.HasCheckConstraint("CK_Registrations_PaymentStatus",
                "[PaymentStatus] IN (N'Pending', N'Confirmed', N'Refunded', N'Cancelled')"));
            e.HasKey(x => x.RegistrationID).HasName("PK_Registrations");
            e.Property(x => x.RegistrationDate).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.BibNumber).HasMaxLength(10);
            e.Property(x => x.PaymentStatus).HasMaxLength(20).IsRequired().HasDefaultValue("Pending");
            e.Property(x => x.EmergencyContactName).HasMaxLength(100);
            e.Property(x => x.EmergencyContactPhone).HasMaxLength(20);
            e.HasIndex(x => new { x.UserID, x.CategoryID }).IsUnique()
                .HasDatabaseName("UQ_Registrations_User_Category");
            e.HasOne(x => x.User).WithMany(u => u.Registrations)
                .HasForeignKey(x => x.UserID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Registrations_Users");
            e.HasOne(x => x.Category).WithMany(c => c.Registrations)
                .HasForeignKey(x => x.CategoryID)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Registrations_Categories");
        });

        modelBuilder.Entity<Result>(e =>
        {
            e.ToTable("Results", t =>
            {
                t.HasCheckConstraint("CK_Results_Status", "[Status] IN (N'Finished', N'DNF', N'DNS', N'DQ')");
                t.HasCheckConstraint("CK_Results_Positions", "[OverallPosition] IS NULL OR [OverallPosition] > 0");
            });
            e.HasKey(x => x.ResultID).HasName("PK_Results");
            e.Property(x => x.FinishTime).HasColumnType("time(0)");
            e.Property(x => x.ChipTime).HasColumnType("time(0)");
            e.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("Finished");
            e.HasIndex(x => x.RegistrationID).IsUnique().HasDatabaseName("UQ_Results_RegistrationID");
            e.HasOne(x => x.Registration).WithOne(r => r.Result)
                .HasForeignKey<Result>(x => x.RegistrationID)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Results_Registrations");
        });
    }
}