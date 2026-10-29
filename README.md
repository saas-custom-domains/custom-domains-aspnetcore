# Custom domains in ASP.NET Core

An example ASP.NET Core 10 app where each customer connects their own domain. It's a small client booking portal, Bookwell: every studio or clinic gets a booking page, and can serve it on `book.theirstudio.com` instead of a Bookwell URL.

It goes with the guide [How to Add Custom Domains to an ASP.NET Core App](https://saascustomdomains.com/blog/posts/custom-domains-aspnetcore).

What's in it:

- One middleware, `CustomDomainMiddleware`, decides per request: your own hostnames get the dashboard, active customer domains get their studio's booking page, anything else gets a 404. It replaces `AllowedHosts` host filtering.
- Requests from the [SaaS Custom Domains](https://saascustomdomains.com) proxy are accepted only with the upstream's auth token, so nobody can claim a customer's domain by sending `X-Served-For` themselves.
- `Url.PageLink()`, `LinkGenerator` and the visitor's IP address work on customer domains behind the proxy (see [Url.PageLink() returns your app's hostname](#urlpagelink-returns-your-apps-hostname)).
- The dashboard and the booking page both live at `/`; a routing policy picks one by whether the request is for a customer domain.
- A Razor Pages booking form with an antiforgery token that works on every customer domain.
- A dashboard to add a domain, check its DNS and remove it. Webhooks update the status.
- [SaaS Custom Domains](https://saascustomdomains.com) issues and renews every customer's TLS certificate and proxies their traffic to the app.

## Run it in 2 minutes

You need Docker.

```bash
git clone https://github.com/saas-custom-domains/custom-domains-aspnetcore
cd custom-domains-aspnetcore
docker compose up --build
```

1. Open http://localhost:3000 and add `acme.localhost` to Acme Physio.
2. Within a few seconds the status changes to **Live**: the mock API "found" the DNS records and sent a webhook.
3. Open http://acme.localhost. The request goes through the mock proxy on port 80, exactly like a real customer domain goes through SaaS Custom Domains, and you see Acme's booking page. Book an appointment: the confirmation page links to `http://acme.localhost/bookings/...`, not to the app's own hostname.

`*.localhost` always resolves to your own machine in Chrome, Firefox, Safari and curl, so there's nothing to add to `/etc/hosts`.

The mock is [scd-mock](https://github.com/saas-custom-domains/scd-mock): a local copy of the SaaS Custom Domains API and proxy, so you can try everything without an account. Production sends `X-Forwarded-Proto: https`; `compose.yaml` sets the mock to send `http`, because it speaks plain HTTP and links should work locally.

### Run the app outside Docker

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Keep the mock in Docker and point it at your dev server:

```bash
SCD_MOCK_UPSTREAM_HOST=host.docker.internal \
SCD_MOCK_WEBHOOK_URL=http://host.docker.internal:3000/webhooks/saascustomdomains \
  docker compose up scd-mock -d
dotnet run --project src/Bookwell
```

`appsettings.Development.json` has the local settings.

## How it works

```
Browser ── book.acme.com ──▶ SaaS Custom Domains ──▶ your ASP.NET Core app
                             (TLS certificate,       Host: app.bookwell.app
                              proxy)                 X-Served-For: book.acme.com
                                                     X-SaaS-Domains-Auth-Token: …
```

| File | What it does |
| --- | --- |
| `src/Bookwell/CustomDomains/CustomDomainMiddleware.cs` | Checks the proxy token, picks the host, finds the studio, sets `Request.Host` |
| `src/Bookwell/CustomDomains/TenantEndpoints.cs` | `TenantEndpointPolicy`: booking pages only on customer domains, dashboard only on yours |
| `src/Bookwell/Pages/Site/` | The booking page and confirmation page |
| `src/Bookwell/Pages/Index.cshtml` | The dashboard |
| `src/Bookwell/CustomDomains/DomainService.cs` | Add, check and remove domains |
| `src/Bookwell/CustomDomains/SaasDomainsClient.cs` | API client for SaaS Custom Domains (typed `HttpClient`) |
| `src/Bookwell/CustomDomains/IntegrationEndpoints.cs` | Webhook endpoint |
| `src/Bookwell/Data/` | EF Core with SQLite; `CustomDomain.Host` has a unique index |
| `src/Bookwell/Program.cs` | The pipeline: health check, `CustomDomainMiddleware`, then routing |

SaaS Custom Domains keeps `Host` and `X-Forwarded-Host` set to your app's own hostname, because that's what hosting platforms route on and what many apps expect (allowed hosts, OAuth callbacks, absolute URLs). The customer's domain arrives separately in `X-Served-For`. The middleware only reads `X-Served-For` when `X-SaaS-Domains-Auth-Token` matches `PROXY_AUTH_TOKEN` (compared with `CryptographicOperations.FixedTimeEquals`), and returns 403 otherwise, including when `PROXY_AUTH_TOKEN` isn't set.

## ASP.NET Core gotchas

### "Bad Request - Invalid Hostname"

`WebApplication` turns on host filtering for you, configured by `AllowedHosts` in `appsettings.json`. The template sets it to `"*"`; security checklists tell you to list your hostnames instead. Any request whose `Host` isn't on the list gets a 400 with this HTML:

```
Bad Request - Invalid Hostname
HTTP Error 400. The request hostname is invalid.
```

Customer traffic from SaaS Custom Domains passes, because `Host` is your own hostname. Host filtering runs before any of your middleware, though, so these get the 400:

- health checks from Docker or a load balancer, which use an IP address;
- webhooks sent to an internal name, like the mock's `Host: app:3000`;
- any request that does carry a customer's domain in `Host`, such as local development on `*.localhost`.

A fixed list can't hold your customers' domains, and host filtering doesn't look at `X-Served-For`. This app keeps `"AllowedHosts": "*"` and does the check itself: `CustomDomainMiddleware` lets your own hostnames (`APP_HOSTS`) through, looks every other host up in the `CustomDomains` table, and returns 404 for unknown and pending domains. The health check (`app.UseHealthChecks("/up")`) runs before it. `FrameworkGotchaTests` checks the 400 and its text.

### Url.PageLink() returns your app's hostname

`Url.PageLink()`, `Url.Action(..., protocol: "https")` and `LinkGenerator.GetUriByPage(HttpContext, ...)` build absolute URLs from `Request.Scheme` and `Request.Host`. Behind the proxy, without anything else, the confirmation page's "manage your booking" link is:

```
http://app.bookwell.app/bookings/0b6c…
```

That's your own hostname, where the customer's booking page doesn't exist, and `http` because the app itself is reached over plain HTTP. The same goes for links in emails, canonical URLs and anything else absolute. Redirects are fine: `RedirectToPage()` sends a relative `Location` (`/bookings/0b6c…`), so the browser stays on the customer's domain.

If your app needs the customer's domain for links like these, hand it `X-Served-For` once the token has proved the request came through the proxy. `CustomDomainMiddleware` does it right after the token check and the domain lookup:

```csharp
request.Host = new HostString(host);
request.Scheme = request.Headers["X-Forwarded-Proto"] == "http" ? "http" : "https";
if (IPAddress.TryParse(request.Headers["X-SaaS-Domains-IP"], out var ip))
{
    context.Connection.RemoteIpAddress = ip;
}
```

After that, everything later in the pipeline sees `https://book.acme.com` and the visitor's IP address. `BookingFormTests` checks the link. (Support can also put the customer's domain in `X-Forwarded-Host`, if you'd rather configure the forwarded headers middleware for it.)

### ForwardedHeaders, KnownProxies and KnownIPNetworks

The forwarded headers middleware is the usual answer for "ASP.NET Core behind a proxy", and it doesn't fit here:

- It only trusts proxies on its lists. The defaults are `KnownProxies = [::1]` and `KnownIPNetworks = [127.0.0.0/8]`, so a proxy anywhere else is ignored. Since ASP.NET Core 8.0.17 and 9.0.6 that applies to every `X-Forwarded-*` header, not only `X-Forwarded-For`.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` clears both lists, which means any client that can reach your app can set `X-Forwarded-Proto` and `X-Forwarded-For`.
- With `ForwardedHeaders.XForwardedHost`, `Request.Host` becomes `X-Forwarded-Host`, which SaaS Custom Domains sets to your own hostname. Nothing changes.
- `ForwardedHostHeaderName = "X-Served-For"` does work, but it trusts the header from any address on your lists, and those would have to list every address the proxy connects from.

This app trusts the proxy by its token instead, which doesn't depend on IP addresses. `FrameworkGotchaTests` runs the middleware with SaaS Custom Domains' headers to show each case.

You may still want the middleware for your own hostname behind your load balancer. In .NET 10, `KnownNetworks` and `Microsoft.AspNetCore.HttpOverrides.IPNetwork` are obsolete, and using them warns:

```
warning ASPDEPR005: 'ForwardedHeadersOptions.KnownNetworks' is obsolete: 'Please use
KnownIPNetworks instead. For more information, visit https://aka.ms/aspnet/deprecate/005.'
```

Use `KnownIPNetworks` with `System.Net.IPNetwork`:

```csharp
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/16"));
app.UseForwardedHeaders(forwarded);
```

Put it before `CustomDomainMiddleware`, which then overrides scheme, host and IP for requests from SaaS Custom Domains.

### Routing runs before your middleware

If you don't call `app.UseRouting()`, `WebApplication` adds it at the start of the pipeline, so the endpoint is chosen before your middleware has looked at the host. Here that meant the dashboard, which also lives at `/`, was rendered on customer domains. `Program.cs` calls `app.UseRouting()` explicitly after `CustomDomainMiddleware`, and `TenantEndpointPolicy` (a `MatcherPolicy`) then picks the booking page or the dashboard. Removing that line fails six tests.

### Antiforgery on custom domains

Razor Pages checks the antiforgery token on every POST, and the booking form works through the proxy with no changes. The check compares the form token with the antiforgery cookie; it doesn't compare `Origin` with `Host` (verified on 10.0.14, with and without the `Request.Host` change). The cookie is host-only, so each customer domain gets its own. Two things to know:

- The tokens are encrypted with Data Protection keys. With more than one instance, or containers that get replaced, store the keys somewhere shared (`PersistKeysToFileSystem`, Redis, Azure Blob Storage), or forms posted to another instance fail. This app keeps them in `DATA_DIR/keys`.
- Take the studio from the domain, never from the form. `SiteModel.OnPostAsync` uses the tenant `CustomDomainMiddleware` found, so a form posted on Acme's domain can't book at Globex.

### Finbuckle.MultiTenant

If you use [Finbuckle.MultiTenant](https://www.finbuckle.com/MultiTenant), its host strategy reads `Request.Host`. Behind the proxy that's your own hostname, so it would find no tenant. Use the whole hostname as the identifier and run the token check and `Request.Host` change before Finbuckle:

```csharp
builder.Services.AddMultiTenant<TenantInfo>()
    .WithHostStrategy("__tenant__")   // the whole host is the identifier
    .WithEFCoreStore<MultiTenantStoreDbContext, TenantInfo>();

app.UseMiddleware<CustomDomainMiddleware>();
app.UseMultiTenant();
```

This app doesn't use Finbuckle; the middleware and `HttpContext.GetTenant()` do the same job in a few lines.

## Use it with your SaaS Custom Domains account

1. [Create an API token](https://app.saascustomdomains.com/api_tokens) and an upstream pointing at your deployed app.
2. Set the environment variables: `SCD_API_TOKEN`, `SCD_ACCOUNT_UUID`, `SCD_UPSTREAM_UUID`, `PROXY_AUTH_TOKEN` (the upstream's auth token), `SCD_WEBHOOK_TOKEN` (from the webhook you create in the dashboard), `APP_HOSTS` and `APP_DOMAIN`.
3. Leave out `SCD_API_URL` so the client talks to the real API.

`APP_HOSTS` must list every hostname your own traffic arrives on: your app's domain, plus any internal name your platform uses to reach the app. The middleware treats any other host as a customer domain and returns 404 if it isn't connected. (In `compose.yaml` that's why `app` is listed: webhooks from the mock arrive as `Host: app:3000`.) The health check at `/up` runs before the middleware, because load balancers call it by IP address.

Your customers point a CNAME at `in.saascustomdomains.com` (or A records at our two static IPs for a root domain). See the [docs](https://docs.saascustomdomains.com).

## Tests

```bash
dotnet test --project tests/Bookwell.Tests      # middleware, routing, form, API client, webhooks
docker compose up -d --build --wait
dotnet build tests/Bookwell.E2E
pwsh tests/Bookwell.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test --project tests/Bookwell.E2E        # end to end, through the mock proxy
```

The unit tests use xUnit v3 and `WebApplicationFactory`, with a fake API. The end-to-end tests use Playwright for .NET with `*.test` domains and send the browser's requests for them to the mock proxy, so nothing depends on DNS or on port 80. To run a second stack side by side, set `APP_PORT`, `SCD_MOCK_API_PORT` and `SCD_MOCK_PROXY_PORT` for both `docker compose` and the end-to-end tests.

CI runs both on every push and once a week, so a new ASP.NET Core release that breaks something shows up here first.

## Tested with

| | Version |
| --- | --- |
| ASP.NET Core | 10.0.14 |
| .NET SDK | 10.0.403 |
| EF Core (SQLite) | 10.0.14 |
| xUnit v3 | 4.0.2 |
| Playwright for .NET | 1.64.1 |

Last checked: 2026-10-29. The unit tests also pass on .NET 11 RC 3.

## Not production-ready on purpose

There's no login. In your app, check in every dashboard handler that the signed-in user owns the studio and the domain. SQLite with `EnsureCreated()` keeps the example small; use your own database and migrations.

## License

MIT
