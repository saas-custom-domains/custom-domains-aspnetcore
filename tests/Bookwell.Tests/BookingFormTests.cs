using System.Net;
using System.Text.RegularExpressions;

namespace Bookwell.Tests;

// The customer-facing form, posted the way it arrives through
// SaaS Custom Domains.
public class BookingFormTests(AppFactory app) : IClassFixture<AppFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> BookAsync(
        HttpClient client, string domain, Action<HttpRequestMessage>? configure = null)
    {
        var page = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/").ThroughProxy(domain), Ct);
        var token = RequestExtensions.AntiforgeryToken(await page.Content.ReadAsStringAsync(Ct));

        var post = new HttpRequestMessage(HttpMethod.Post, "/").ThroughProxy(domain);
        post.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Form.Service"] = "Initial assessment (45 min)",
            ["Form.Date"] = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd"),
            ["Form.Time"] = "10:30",
            ["Form.Name"] = "Sam Reader",
            ["Form.Email"] = "sam@example.com",
            ["__RequestVerificationToken"] = token,
        });
        configure?.Invoke(post);
        return await client.SendAsync(post, Ct);
    }

    [Fact]
    public async Task Books_through_the_proxy_and_links_to_the_customer_domain()
    {
        await app.AddDomainAsync("acme", "book.acme-form.test");
        var client = app.Client();

        var res = await BookAsync(client, "book.acme-form.test");

        // A relative redirect, so the browser stays on the customer's domain.
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var location = res.Headers.Location!.ToString();
        Assert.Matches("^/bookings/[0-9a-f-]{36}$", location);

        var confirmation = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, location).ThroughProxy("book.acme-form.test"), Ct);
        var html = await confirmation.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Booked: Initial assessment (45 min)", html);

        // Url.PageLink() uses the visitor's domain and scheme, not
        // Host: bookwell.app. Without CustomDomainMiddleware setting
        // Request.Host and Request.Scheme, this is http://bookwell.app/...
        var link = Regex.Match(html, "data-testid=\"manage-link\" href=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal($"https://book.acme-form.test{location}", link);
    }

    [Fact]
    public async Task Rejects_a_post_without_the_antiforgery_token()
    {
        await app.AddDomainAsync("acme", "book.acme-csrf.test");
        var post = new HttpRequestMessage(HttpMethod.Post, "/").ThroughProxy("book.acme-csrf.test");
        post.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Form.Name"] = "x" });

        var res = await app.Client().SendAsync(post, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Accepts_the_browsers_Origin_on_the_customer_domain()
    {
        // Browsers send Origin: https://<customer domain>. ASP.NET Core 10
        // checks only the antiforgery token, not Origin, so this passes
        // either way today. If a check that compares Origin with Host
        // comes along, it passes too: by the time the form is handled,
        // Host is the customer's domain.
        await app.AddDomainAsync("acme", "book.acme-origin.test");

        var res = await BookAsync(app.Client(), "book.acme-origin.test",
            post => post.Headers.Add("Origin", "https://book.acme-origin.test"));

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
    }

    [Fact]
    public async Task Never_shows_one_studios_booking_on_another_studios_domain()
    {
        await app.AddDomainAsync("acme", "book.acme-own.test");
        await app.AddDomainAsync("globex", "yoga.globex-own.test");
        var client = app.Client();
        var location = (await BookAsync(client, "book.acme-own.test")).Headers.Location!.ToString();

        var res = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, location).ThroughProxy("yoga.globex-own.test"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
