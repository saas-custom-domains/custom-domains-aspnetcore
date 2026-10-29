using Microsoft.AspNetCore.Routing.Matching;

namespace Bookwell.CustomDomains;

// Marks endpoints that only exist on customer domains (the booking
// site).
public sealed class TenantSiteAttribute : Attribute;

// Marks endpoints that only exist on your own hostnames (the dashboard,
// the webhook endpoint).
public sealed class AppHostOnlyAttribute : Attribute;

// Lets the dashboard and the booking site both live at "/": routing
// picks between them by whether CustomDomainMiddleware found a tenant.
// It also makes the booking pages unreachable on your own hostname,
// and the dashboard unreachable on a customer's domain (404).
// Endpoints with neither attribute, like static files, match on both.
public sealed class TenantEndpointPolicy
    : MatcherPolicy, IEndpointSelectorPolicy
{
    public override int Order => 0;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) =>
        endpoints.Any(e =>
            e.Metadata.GetMetadata<TenantSiteAttribute>() is not null
            || e.Metadata.GetMetadata<AppHostOnlyAttribute>()
                is not null);

    public Task ApplyAsync(HttpContext context, CandidateSet candidates)
    {
        var onCustomerDomain = context.GetTenant() is not null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var metadata = candidates[i].Endpoint.Metadata;
            var allowed = onCustomerDomain
                ? metadata.GetMetadata<AppHostOnlyAttribute>() is null
                : metadata.GetMetadata<TenantSiteAttribute>() is null;
            if (!allowed) candidates.SetValidity(i, false);
        }
        return Task.CompletedTask;
    }
}
