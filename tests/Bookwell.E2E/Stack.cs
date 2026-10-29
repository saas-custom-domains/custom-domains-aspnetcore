using Microsoft.Playwright;

namespace Bookwell.E2E;

// Runs against `docker compose up`. The browser sends every request for
// a customer domain (*.test) to the mock proxy, the way DNS sends a real
// customer domain to SaaS Custom Domains. Host and Origin stay
// "acme.test" whatever port the mock listens on, so several stacks can
// run side by side.
public static class Stack
{
    public static readonly string AppUrl = Env("APP_URL")
        ?? $"http://localhost:{Env("APP_PORT") ?? "3000"}";

    public static readonly string ProxyUrl = Env("PROXY_URL")
        ?? $"http://127.0.0.1:{Env("SCD_MOCK_PROXY_PORT") ?? "80"}";

    public static BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = AppUrl,
        Proxy = new Proxy { Server = ProxyUrl, Bypass = new Uri(AppUrl).Host },
    };

    // The mock proxy speaks plain HTTP (production is HTTPS).
    public static string CustomerUrl(string host, string path = "/") => $"http://{host}{path}";

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
