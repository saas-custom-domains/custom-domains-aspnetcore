using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Bookwell.E2E;

public class CustomDomainsTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => Stack.ContextOptions();

    private async Task<ILocator> AddDomainAsync(string studio, string host)
    {
        var section = Page.GetByTestId($"studio-{studio}");
        await Page.GotoAsync("/");
        await section.GetByLabel("Domain", new() { Exact = true }).FillAsync(host);
        await section.GetByRole(AriaRole.Button, new() { Name = "Add domain" }).ClickAsync();
        var row = Page.GetByTestId($"domain-{host}");
        await Expect(row).ToBeVisibleAsync();
        return row;
    }

    [Fact]
    public async Task Connects_a_domain_serves_the_booking_page_through_the_proxy_and_removes_it()
    {
        var host = $"acme-{DateTime.UtcNow.Ticks}.test";
        var row = await AddDomainAsync("acme", host);

        // The mock marks *.test as pointed after ~2s and sends webhooks.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!(await row.TextContentAsync() ?? "").Contains("Live") && DateTime.UtcNow < deadline)
        {
            await Page.WaitForTimeoutAsync(500);
            await Page.ReloadAsync();
        }
        await Expect(row).ToContainTextAsync("Live");

        await Page.GotoAsync(Stack.CustomerUrl(host));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Acme Physio" })).ToBeVisibleAsync();
        await Expect(Page).ToHaveTitleAsync("Acme Physio");

        // A Razor Pages form with an antiforgery token, through the proxy.
        await Page.GetByLabel("Service").SelectOptionAsync("Follow-up (30 min)");
        await Page.GetByLabel("Time").SelectOptionAsync("14:00");
        await Page.GetByLabel("Name").FillAsync("Sam Reader");
        await Page.GetByLabel("Email").FillAsync("sam@example.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Book" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Booked: Follow-up (30 min)");
        await Expect(Page).ToHaveURLAsync(new Regex($"^http://{Regex.Escape(host)}/bookings/"));
        // The absolute link is built on the customer's domain, not the app's.
        var link = Page.GetByTestId("manage-link");
        await Expect(link).ToHaveAttributeAsync("href", Page.Url);

        await Page.GotoAsync("/");
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(row).ToBeHiddenAsync();
        var gone = await Page.GotoAsync(Stack.CustomerUrl(host));
        Assert.Equal(404, gone?.Status);
    }

    [Fact]
    public async Task A_domain_that_does_not_point_at_the_proxy_stays_pending()
    {
        var host = $"book-{DateTime.UtcNow.Ticks}.example.com";
        var row = await AddDomainAsync("globex", host);

        await row.GetByRole(AriaRole.Button, new() { Name = "Check DNS" }).ClickAsync();
        await Expect(row).ToContainTextAsync("Waiting for DNS");

        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await Expect(row).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Rejects_our_own_domains()
    {
        var section = Page.GetByTestId("studio-acme");
        await Page.GotoAsync("/");
        await section.GetByLabel("Domain", new() { Exact = true }).FillAsync("admin.bookwell.app");
        await section.GetByRole(AriaRole.Button, new() { Name = "Add domain" }).ClickAsync();

        await Expect(Page.GetByTestId("studio-acme").GetByRole(AriaRole.Alert))
            .ToHaveTextAsync("That's one of our domains.");
    }

    [Fact]
    public async Task Requests_that_skip_the_proxy_cannot_claim_a_domain()
    {
        // Straight to the app, not through the proxy.
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = Stack.AppUrl });

        var spoofed = await api.GetAsync("/", new()
        {
            Headers = new Dictionary<string, string> { ["X-Served-For"] = "acme.test" },
        });
        var wrongToken = await api.GetAsync("/", new()
        {
            Headers = new Dictionary<string, string>
            {
                ["X-Served-For"] = "acme.test",
                ["X-SaaS-Domains-Auth-Token"] = "guess",
            },
        });

        Assert.Equal(403, spoofed.Status);
        Assert.Equal(403, wrongToken.Status);
    }

    [Fact]
    public async Task Webhooks_need_the_right_token()
    {
        var api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = Stack.AppUrl });

        var res = await api.PostAsync("/webhooks/saascustomdomains?token=guess", new()
        {
            DataObject = new { @event = "domain_dns_proxy_record_valid", domain = new { uuid = "x" } },
        });

        Assert.Equal(401, res.Status);
    }

    [Fact]
    public async Task Unknown_domains_get_a_404()
    {
        var res = await Page.GotoAsync(Stack.CustomerUrl("unknown.test"));
        Assert.Equal(404, res?.Status);
    }
}
