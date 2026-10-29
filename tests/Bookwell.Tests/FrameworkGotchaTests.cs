using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;

namespace Bookwell.Tests;

// What ASP.NET Core does on its own with custom domains. If a release
// changes any of this, these tests fail and the README needs an update.
public class FrameworkGotchaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AllowedHosts_answers_anything_but_the_listed_hosts_with_400_Invalid_Hostname()
    {
        await using var app = new AppFactory();
        app.Settings["AllowedHosts"] = "bookwell.app";
        await app.AddDomainAsync("acme", "book.acme.test");
        var client = app.Client();

        // SaaS Custom Domains sends your own hostname in Host, so it passes.
        var proxied = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/").ThroughProxy("book.acme.test"), Ct);
        // Host filtering runs before everything else, so health checks by
        // IP address and webhooks sent to an internal name get the 400...
        var health = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/up").WithHost("10.0.3.17:3000"), Ct);
        var webhook = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/webhooks/saascustomdomains?token={AppFactory.WebhookToken}")
                .WithHost("app:3000"), Ct);
        // ...and so does a customer's domain arriving in Host (local
        // development on *.localhost, or X-Forwarded-Host set to it).
        var direct = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/").WithHost("book.acme.test"), Ct);

        Assert.Equal(HttpStatusCode.OK, proxied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, health.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, webhook.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
        var body = await direct.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<h2>Bad Request - Invalid Hostname</h2>", body);
        Assert.Contains("HTTP Error 400. The request hostname is invalid.", body);
    }

    [Fact]
    public async Task ForwardedHeaders_ignores_proxies_it_does_not_know()
    {
        // Defaults: only loopback is trusted, so a proxy on another
        // address (Docker, a load balancer, SaaS Custom Domains) is ignored.
        var seen = await SeenByAppAsync(_ => { }, from: "172.18.0.3");

        Assert.Equal("http://bookwell.app from 172.18.0.3", seen);
    }

    [Fact]
    public async Task ForwardedHeaders_XForwardedHost_is_the_apps_own_host()
    {
        // Trusted: scheme and client IP are right, but X-Forwarded-Host
        // is your own hostname, so Host doesn't change.
        var seen = await SeenByAppAsync(
            o => o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12")), from: "172.18.0.3");

        Assert.Equal("https://bookwell.app from 203.0.113.7", seen);
    }

    [Fact]
    public async Task ForwardedHeaders_can_read_X_Served_For_but_trusts_by_IP()
    {
        // Possible, but only as safe as your list of proxy addresses:
        // from any trusted address, X-Served-For is taken as is.
        var seen = await SeenByAppAsync(o =>
        {
            o.ForwardedHostHeaderName = "X-Served-For";
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        }, from: "172.18.0.3");

        Assert.Equal("https://book.acme.test from 203.0.113.7", seen);
    }

    private static async Task<string> SeenByAppAsync(Action<ForwardedHeadersOptions> configure, string from)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
        };
        configure(options);
        app.UseForwardedHeaders(options);
        app.Run(ctx => ctx.Response.WriteAsync(
            $"{ctx.Request.Scheme}://{ctx.Request.Host} from {ctx.Connection.RemoteIpAddress}"));
        await app.StartAsync(Ct);

        // Headers exactly as SaaS Custom Domains sends them.
        var context = await app.GetTestServer().SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = "/";
            ctx.Request.Host = new HostString("bookwell.app");
            ctx.Request.Headers["X-Served-For"] = "book.acme.test";
            ctx.Request.Headers["X-Forwarded-Host"] = "bookwell.app";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
            ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(from);
        }, Ct);
        return await new StreamReader(context.Response.Body).ReadToEndAsync(Ct);
    }
}
