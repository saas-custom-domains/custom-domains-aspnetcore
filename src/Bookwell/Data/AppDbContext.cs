using Microsoft.EntityFrameworkCore;

namespace Bookwell.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<Studio> Studios => Set<Studio>();
    public DbSet<CustomDomain> CustomDomains => Set<CustomDomain>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Studio>().HasIndex(s => s.Slug).IsUnique();

        // Every customer-facing request looks a domain up by host,
        // and a host belongs to one studio at most.
        var domain = model.Entity<CustomDomain>();
        domain.HasIndex(d => d.Host).IsUnique();
        domain.HasIndex(d => d.ProviderUuid).IsUnique();
        domain.Property(d => d.Status).HasConversion<string>();

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
                Tagline = "Sports injuries and back pain.",
                Services = [
                    "Initial assessment (45 min)",
                    "Follow-up (30 min)",
                ],
            },
            new Studio
            {
                Slug = "globex",
                Name = "Globex Yoga",
                Tagline = "Small classes in the old mill.",
                Services = [
                    "Private session (60 min)",
                    "Beginners class (75 min)",
                ],
            });
        db.SaveChanges();
    }
}
