using System.Net;
using Bookwell.Data;

namespace Bookwell.Tests;

public class CustomDomainMiddlewareTests(AppFactory app) : IClassFixture<AppFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = app.Client();

    private Task<HttpResponseMessage> GetAsync(HttpRequestMessage request) =>
        _client.SendAsync(request, Ct);

    private static HttpRequestMessage Get(string path = "/") => new(HttpMethod.Get, path);

    [Fact]
    public async Task Serves_the_dashboard_on_app_hosts()
    {
        foreach (var host in new[] { "localhost", "bookwell.app", "BOOKWELL.app:8443" })
        {
            var res = await GetAsync(Get().WithHost(host));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Contains("Bookwell dashboard", await res.Content.ReadAsStringAsync(Ct));
        }
    }

    [Fact]
    public async Task Serves_an_active_domain_its_studio()
    {
        await app.AddDomainAsync("acme", "book.acme-direct.test");

        var res = await GetAsync(Get().WithHost("book.acme-direct.test"));

        var html = await res.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<title>Acme Physio</title>", html);
        Assert.DoesNotContain("Bookwell dashboard", html);
    }

    [Fact]
    public async Task Reads_X_Served_For_with_the_right_token()
    {
        await app.AddDomainAsync("globex", "yoga.globex-proxied.test");

        var res = await GetAsync(Get().ThroughProxy("yoga.globex-proxied.test"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<title>Globex Yoga</title>", await res.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Rejects_X_Served_For_without_the_right_token()
    {
        await app.AddDomainAsync("acme", "book.acme-spoofed.test");

        var missing = await GetAsync(Get().ThroughProxy("book.acme-spoofed.test", token: null));
        var wrong = await GetAsync(Get().ThroughProxy("book.acme-spoofed.test", token: "guess"));

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
    }

    [Fact]
    public async Task Rejects_X_Served_For_when_no_token_is_configured()
    {
        await using var unconfigured = new AppFactory();
        unconfigured.Settings["PROXY_AUTH_TOKEN"] = null;
        await unconfigured.AddDomainAsync("acme", "book.acme.test");

        var res = await unconfigured.Client().SendAsync(
            Get().ThroughProxy("book.acme.test", token: ""), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Returns_404_for_pending_and_unknown_domains()
    {
        await app.AddDomainAsync("acme", "book.acme-pending.test", DomainStatus.Pending);

        var pending = await GetAsync(Get().WithHost("book.acme-pending.test"));
        var unknown = await GetAsync(Get().WithHost("evil.example"));
        var proxiedUnknown = await GetAsync(Get().ThroughProxy("evil.example"));

        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, proxiedUnknown.StatusCode);
        Assert.Equal("Unknown domain", await unknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Keeps_the_booking_site_and_the_dashboard_apart()
    {
        await app.AddDomainAsync("acme", "book.acme-apart.test");

        // Booking pages don't exist on the app's own hostname...
        var bookingOnAppHost = await GetAsync(Get($"/bookings/{Guid.NewGuid()}").WithHost("localhost"));
        // ...and the dashboard's endpoints don't exist on customer domains.
        var webhookOnCustomerDomain = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/webhooks/saascustomdomains?token={AppFactory.WebhookToken}")
                .WithHost("book.acme-apart.test"),
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, bookingOnAppHost.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, webhookOnCustomerDomain.StatusCode);
    }

    [Fact]
    public async Task Answers_health_checks_on_any_host()
    {
        // Load balancers and Docker call it by IP address.
        var res = await GetAsync(Get("/up").WithHost("10.0.3.17:3000"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Serves_static_files_on_customer_domains()
    {
        await app.AddDomainAsync("acme", "book.acme-static.test");

        var res = await GetAsync(Get("/site.css").ThroughProxy("book.acme-static.test"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
