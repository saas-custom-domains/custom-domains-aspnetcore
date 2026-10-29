using System.Net;
using System.Text.RegularExpressions;
using Bookwell.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bookwell.Tests;

// The whole app in memory, with its own SQLite file and a fake
// SaaS Custom Domains API.
public class AppFactory : WebApplicationFactory<Program>
{
    public const string ProxyToken = "test_proxy_token";
    public const string WebhookToken = "test_webhook_token";

    private readonly string _dataDir = Directory.CreateTempSubdirectory("bookwell-tests-").FullName;

    public FakeScdApi Api { get; } = new();

    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["APP_HOSTS"] = "localhost,bookwell.app",
        ["APP_DOMAIN"] = "bookwell.app",
        ["SCD_API_URL"] = "http://scd.test",
        ["SCD_API_TOKEN"] = "scd_test",
        ["SCD_ACCOUNT_UUID"] = "acc_test",
        ["SCD_UPSTREAM_UUID"] = "upstream_test",
        ["PROXY_AUTH_TOKEN"] = ProxyToken,
        ["SCD_WEBHOOK_TOKEN"] = WebhookToken,
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATA_DIR", _dataDir);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
            services.AddHttpClient<CustomDomains.SaasDomainsClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Api));
    }

    public HttpClient Client() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<CustomDomain> AddDomainAsync(
        string studioSlug, string host, DomainStatus status = DomainStatus.Active, string? uuid = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var studio = await db.Studios.SingleAsync(s => s.Slug == studioSlug);
        var domain = new CustomDomain { StudioId = studio.Id, Host = host, Status = status, ProviderUuid = uuid };
        db.CustomDomains.Add(domain);
        await db.SaveChangesAsync();
        return domain;
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dataDir, recursive: true); } catch (IOException) { }
    }
}

public static class RequestExtensions
{
    // A request as SaaS Custom Domains forwards it: Host is the app's own
    // hostname, the customer's domain is in X-Served-For.
    public static HttpRequestMessage ThroughProxy(
        this HttpRequestMessage request, string customerDomain, string? token = AppFactory.ProxyToken)
    {
        request.Headers.Host = "bookwell.app";
        request.Headers.Add("X-Served-For", customerDomain);
        request.Headers.Add("X-Forwarded-Host", "bookwell.app");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-SaaS-Domains-IP", "203.0.113.7");
        if (token is not null) request.Headers.Add("X-SaaS-Domains-Auth-Token", token);
        return request;
    }

    public static HttpRequestMessage WithHost(this HttpRequestMessage request, string host)
    {
        request.Headers.Host = host;
        return request;
    }

    public static string AntiforgeryToken(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")
            .Groups[1].Value;
}

// Stands in for the SaaS Custom Domains API in unit tests.
public sealed class FakeScdApi : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? Auth, string? Body)> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath,
            request.Headers.Authorization?.ToString(), body));
        return Respond?.Invoke(request) ?? Json(HttpStatusCode.Created,
            """{"uuid":"dom_123","host":"x","status":"pending_check"}""");
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
