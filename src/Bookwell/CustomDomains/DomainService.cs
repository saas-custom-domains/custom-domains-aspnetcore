using Bookwell.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwell.CustomDomains;

// Add, check and remove custom domains. SaaS Custom Domains checks
// DNS and issues certificates; this keeps the local rows in step.
public sealed class DomainService(
    AppDbContext db,
    SaasDomainsClient api,
    IOptions<CustomDomainOptions> options,
    ILogger<DomainService> logger)
{
    private readonly CustomDomainOptions _options = options.Value;

    // Returns an error message for the dashboard, or null on success.
    public async Task<string?> AddAsync(int studioId, string input, string? dnsContact)
    {
        var host = Hosts.Normalize(input);
        if (!Hosts.IsValid(host)) return $"\"{host}\" isn't a valid domain.";
        if (_options.IsReserved(host)) return "That's one of our domains.";
        if (await db.CustomDomains.AnyAsync(d => d.Host == host))
        {
            return $"{host} is already connected to a studio.";
        }

        var domain = new CustomDomain { StudioId = studioId, Host = host };
        db.CustomDomains.Add(domain);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost a race with another request for the same host.
            return $"{host} is already connected to a studio.";
        }
        try
        {
            var created = await api.CreateDomainAsync(host, dnsContact);
            domain.ProviderUuid = created.Uuid;
            await db.SaveChangesAsync();
            return null;
        }
        catch (Exception e)
        {
            // Don't keep a row the proxy doesn't know about.
            db.CustomDomains.Remove(domain);
            await db.SaveChangesAsync();
            if (e is SaasDomainsException { ErrorCode: "host_taken" })
            {
                return $"{host} is already connected to another account.";
            }
            logger.LogError(e, "Could not add {Host}", host);
            return "Could not add the domain. Try again.";
        }
    }

    public async Task<bool> CheckAsync(CustomDomain domain)
    {
        if (domain.ProviderUuid is null) return false;

        var result = await api.VerifyDnsAsync(domain.ProviderUuid);
        var valid = result.DnsStatus == "valid";
        domain.Status = valid ? DomainStatus.Active : DomainStatus.Pending;
        await db.SaveChangesAsync();
        return valid;
    }

    public async Task RemoveAsync(CustomDomain domain)
    {
        // Proxy first, database second: if the API call fails, the row
        // is still here and the user can retry.
        if (domain.ProviderUuid is not null)
        {
            await api.DeleteDomainAsync(domain.ProviderUuid);
        }
        db.CustomDomains.Remove(domain);
        await db.SaveChangesAsync();
    }
}
