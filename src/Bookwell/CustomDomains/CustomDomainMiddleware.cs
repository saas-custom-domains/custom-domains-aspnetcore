using System.Net;
using Bookwell.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwell.CustomDomains;

// The studio a request is for, when it arrived on a customer's domain.
public sealed record Tenant(int StudioId, string Name, string Host);

public static class TenantExtensions
{
    public static Tenant? GetTenant(this HttpContext context) =>
        context.Features.Get<Tenant>();
}

// Decides, once per request, who the request is for: your own
// hostnames (APP_HOSTS) get the dashboard, active customer domains get
// their studio's booking site, and anything else gets a 404.
public sealed class CustomDomainMiddleware(
    RequestDelegate next,
    IOptions<CustomDomainOptions> options)
{
    private readonly CustomDomainOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var request = context.Request;

        // Behind SaaS Custom Domains, Host is your own hostname and the
        // customer's domain arrives in X-Served-For. Only trust it with
        // the upstream's auth token, or anyone could claim any domain.
        var proxied = request.Headers.ContainsKey("X-Served-For");
        if (proxied && !HasValidProxyToken(request))
        {
            await Results.Text("Forbidden", statusCode: 403).ExecuteAsync(context);
            return;
        }

        var host = Hosts.Normalize(
            proxied ? request.Headers["X-Served-For"] : request.Host.Host);

        if (_options.IsAppHost(host))
        {
            await next(context);
            return;
        }

        var tenant = await db.CustomDomains
            .Where(d => d.Host == host && d.Status == DomainStatus.Active)
            .Select(d => new Tenant(d.StudioId, d.Studio.Name, d.Host))
            .SingleOrDefaultAsync(context.RequestAborted);
        if (tenant is null)
        {
            await Results.Text("Unknown domain", statusCode: 404).ExecuteAsync(context);
            return;
        }
        context.Features.Set(tenant);

        if (proxied)
        {
            // The token proved the request came through the proxy, so
            // describe it the way the visitor sent it. From here on,
            // Url.PageLink(), LinkGenerator and anything else that reads
            // Request.Host, Request.Scheme or RemoteIpAddress sees
            // https://<customer domain> and the visitor's IP.
            request.Host = new HostString(host);
            request.Scheme = request.Headers["X-Forwarded-Proto"] == "http" ? "http" : "https";
            if (IPAddress.TryParse(request.Headers["X-SaaS-Domains-IP"], out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }
        }

        await next(context);
    }

    private bool HasValidProxyToken(HttpRequest request) =>
        Tokens.Matches(
            request.Headers["X-SaaS-Domains-Auth-Token"], _options.ProxyAuthToken);
}
