using System.Text.RegularExpressions;

namespace Bookwell.CustomDomains;

public static partial class Hosts
{
    // "Acme.Example.com." and "acme.example.com:443" are the same host.
    public static string Normalize(string? input)
    {
        var host = (input ?? "").Trim().ToLowerInvariant();
        host = PortSuffix().Replace(host, "");
        return host.TrimEnd('.');
    }

    public static bool IsValid(string host) =>
        host.Length <= 253
        && host.Split('.') is { Length: >= 2 } labels
        && labels.All(Label().IsMatch);

    [GeneratedRegex(@":\d+$")]
    private static partial Regex PortSuffix();

    [GeneratedRegex(@"^(?!-)[a-z0-9-]{1,63}(?<!-)$")]
    private static partial Regex Label();
}

public sealed class CustomDomainOptions
{
    // Hostnames that serve the dashboard, not a customer site.
    public HashSet<string> AppHosts { get; set; } = ["localhost"];

    // The domain your customers can't claim (e.g. "bookwell.app").
    public string? AppDomain { get; set; }

    // The upstream's auth token: X-SaaS-Domains-Auth-Token.
    public string? ProxyAuthToken { get; set; }

    // The token on your webhook: ?token= on every webhook request.
    public string? WebhookToken { get; set; }

    public bool IsAppHost(string host) => AppHosts.Contains(host);

    public bool IsReserved(string host) =>
        IsAppHost(host)
        || (!string.IsNullOrEmpty(AppDomain)
            && (host == AppDomain || host.EndsWith("." + AppDomain)));

    public static CustomDomainOptions FromConfiguration(
        IConfiguration config)
    {
        return new CustomDomainOptions
        {
            AppHosts = (config["APP_HOSTS"] ?? "localhost")
                .Split(',')
                .Select(Hosts.Normalize)
                .Where(h => h.Length > 0)
                .ToHashSet(),
            AppDomain = Hosts.Normalize(config["APP_DOMAIN"]),
            ProxyAuthToken = config["PROXY_AUTH_TOKEN"],
            WebhookToken = config["SCD_WEBHOOK_TOKEN"],
        };
    }
}
