using Microsoft.EntityFrameworkCore;

namespace Bookwell.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Studio> Studios => Set<Studio>();
    public DbSet<CustomDomain> CustomDomains => Set<CustomDomain>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Studio>().HasIndex(s => s.Slug).IsUnique();

        // Every customer-facing request looks a domain up by host,
        // and a host belongs to one studio at most.
        model.Entity<CustomDomain>().HasIndex(d => d.Host).IsUnique();
        model.Entity<CustomDomain>().HasIndex(d => d.ProviderUuid).IsUnique();
        model.Entity<CustomDomain>().Property(d => d.Status).HasConversion<string>();

        model.Entity<Booking>().HasIndex(b => b.Reference).IsUnique();
    }

    public static void Seed(DbContext db, bool _)
    {
        var studios = db.Set<Studio>();
        if (studios.Any()) return;

        studios.AddRange(
            new Studio
            {
                Slug = "acme",
                Name = "Acme Physio",
                Tagline = "Sports injuries and back pain. Same-week appointments.",
                Services = ["Initial assessment (45 min)", "Follow-up (30 min)"],
            },
            new Studio
            {
                Slug = "globex",
                Name = "Globex Yoga",
                Tagline = "Small classes and private sessions in the old mill.",
                Services = ["Private session (60 min)", "Beginners class (75 min)"],
            });
        db.SaveChanges();
    }
}
