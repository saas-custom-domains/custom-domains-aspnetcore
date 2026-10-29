using System.Net;
using System.Net.Http.Json;
using Bookwell.Data;
using Microsoft.EntityFrameworkCore;

namespace Bookwell.Tests;

// The dashboard, the API client and webhooks.
public class DomainManagementTests(AppFactory app) : IClassFixture<AppFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> AddViaDashboardAsync(string host, string? dnsContact = null)
    {
        var client = app.Client();
        var page = await client.GetStringAsync("/", Ct);
        var form = new Dictionary<string, string>
        {
            ["studioId"] = "1",
            ["host"] = host,
            ["__RequestVerificationToken"] = RequestExtensions.AntiforgeryToken(page),
        };
        if (dnsContact is not null) form["dnsContact"] = dnsContact;
        return await client.PostAsync("/?handler=Add", new FormUrlEncodedContent(form), Ct);
    }

    private Task<CustomDomain?> FindAsync(string host) =>
        app.QueryAsync(db => db.CustomDomains.SingleOrDefaultAsync(d => d.Host == host));

    [Fact]
    public async Task Adds_a_domain_through_the_API()
    {
        app.Api.Respond = req => FakeScdApi.Json(HttpStatusCode.Created,
            """{"uuid":"dom_add","host":"book.acme-add.test","status":"pending_check"}""");

        var res = await AddViaDashboardAsync(" Book.Acme-Add.test. ", "it@acme.test");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var domain = await FindAsync("book.acme-add.test");
        Assert.Equal("dom_add", domain?.ProviderUuid);
        Assert.Equal(DomainStatus.Pending, domain?.Status);

        var call = app.Api.Requests.Last();
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/api/v1/accounts/acc_test/upstreams/upstream_test/custom_domains", call.Path);
        Assert.Equal("Bearer scd_test", call.Auth);
        Assert.Equal("""{"host":"book.acme-add.test","instructions_recipient":"it@acme.test"}""", call.Body);
    }

    [Fact]
    public async Task Does_not_keep_a_domain_the_API_rejected()
    {
        app.Api.Respond = _ => FakeScdApi.Json(HttpStatusCode.BadRequest,
            """{"error":"Host has already been taken","error_code":"host_taken"}""");

        var res = await AddViaDashboardAsync("book.acme-taken.test");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("connected to another account", await res.Content.ReadAsStringAsync(Ct));
        Assert.Null(await FindAsync("book.acme-taken.test"));
    }

    [Theory]
    [InlineData("admin.bookwell.app", "That&#x27;s one of our domains.")]
    [InlineData("localhost", "isn&#x27;t a valid domain")]
    [InlineData("not a domain", "isn&#x27;t a valid domain")]
    public async Task Rejects_reserved_and_invalid_domains(string host, string error)
    {
        var res = await AddViaDashboardAsync(host);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(error, await res.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Rejects_a_domain_another_studio_has()
    {
        await app.AddDomainAsync("globex", "book.dupe.test");

        var res = await AddViaDashboardAsync("book.dupe.test");

        Assert.Contains("already connected to a studio", await res.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Removing_treats_404_from_the_API_as_done()
    {
        var domain = await app.AddDomainAsync("acme", "book.acme-gone.test", uuid: "dom_gone");
        app.Api.Respond = _ => FakeScdApi.Json(HttpStatusCode.NotFound,
            """{"error":"Custom domain dom_gone not found.","error_code":"not_found"}""");
        var client = app.Client();
        var page = await client.GetStringAsync("/", Ct);

        var res = await client.PostAsync($"/?handler=Remove&id={domain.Id}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = RequestExtensions.AntiforgeryToken(page) }), Ct);

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Null(await FindAsync("book.acme-gone.test"));
        Assert.Equal(HttpMethod.Delete, app.Api.Requests.Last().Method);
    }

    [Fact]
    public async Task Webhooks_need_the_right_token()
    {
        var client = app.Client();
        var body = new { @event = "domain_dns_proxy_record_valid", domain = new { uuid = "x" } };

        var wrong = await client.PostAsJsonAsync("/webhooks/saascustomdomains?token=guess", body, Ct);
        var missing = await client.PostAsJsonAsync("/webhooks/saascustomdomains", body, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
    }

    [Fact]
    public async Task Webhooks_update_the_status()
    {
        await app.AddDomainAsync("acme", "book.acme-hook.test", DomainStatus.Pending, uuid: "dom_hook");
        var client = app.Client();
        var url = $"/webhooks/saascustomdomains?token={AppFactory.WebhookToken}";

        foreach (var e in new[] { "domain_dns_proxy_record_valid", "tls_certificate_issued" })
        {
            var res = await client.PostAsJsonAsync(url,
                new { @event = e, domain = new { uuid = "dom_hook", host = "book.acme-hook.test" } }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        }

        var domain = await FindAsync("book.acme-hook.test");
        Assert.Equal(DomainStatus.Active, domain?.Status);
        Assert.True(domain?.HttpsReady);

        // Deleted in the SaaS Custom Domains dashboard: gone here too.
        var deleted = await client.PostAsJsonAsync(url,
            new { @event = "domain_deleted", domain = new { uuid = "dom_hook" } }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Null(await FindAsync("book.acme-hook.test"));

        // Someone else's domain: acknowledged, nothing changes.
        var unknown = await client.PostAsJsonAsync(url,
            new { @event = "domain_deleted", domain = new { uuid = "dom_other" } }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
    }
}
