namespace Bookwell.Data;

// A studio or clinic that takes bookings: one tenant.
public class Studio
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public required string Tagline { get; set; }
    public List<string> Services { get; set; } = [];
    public List<CustomDomain> Domains { get; set; } = [];
}

public enum DomainStatus { Pending, Active }

public class CustomDomain
{
    public int Id { get; set; }
    public int StudioId { get; set; }
    public Studio Studio { get; set; } = null!;

    // Normalized (lowercase, no port, no trailing dot). Unique.
    public required string Host { get; set; }
    public DomainStatus Status { get; set; } = DomainStatus.Pending;
    public bool HttpsReady { get; set; }

    // The domain's UUID at SaaS Custom Domains.
    public string? ProviderUuid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Booking
{
    public int Id { get; set; }
    // Goes in the confirmation link, so it can't be guessed.
    public Guid Reference { get; set; } = Guid.NewGuid();
    public int StudioId { get; set; }
    public required string Service { get; set; }
    public DateTime StartsAt { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
}
