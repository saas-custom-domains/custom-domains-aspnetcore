using System.Net;
using System.Text.Json;

namespace Bookwell.CustomDomains;

// A small client for the SaaS Custom Domains API, as a typed HttpClient.
// Docs: https://docs.saascustomdomains.com/custom-domains
public sealed class SaasDomainsClient(HttpClient http, IConfiguration config)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private string DomainsPath =>
        $"api/v1/accounts/{config["SCD_ACCOUNT_UUID"]}" +
        $"/upstreams/{config["SCD_UPSTREAM_UUID"]}/custom_domains";

    public Task<ProviderDomain> CreateDomainAsync(
        string host, string? instructionsRecipient, CancellationToken ct = default) =>
        SendAsync<ProviderDomain>(HttpMethod.Post, DomainsPath,
            // We email this address the exact DNS records to add.
            new { host, instructions_recipient = instructionsRecipient }, ct);

    public Task<DnsVerification> VerifyDnsAsync(string uuid, CancellationToken ct = default) =>
        SendAsync<DnsVerification>(HttpMethod.Post, $"{DomainsPath}/{uuid}/verify_dns_records", null, ct);

    public async Task DeleteDomainAsync(string uuid, CancellationToken ct = default)
    {
        try
        {
            await SendAsync<JsonElement>(HttpMethod.Delete, $"{DomainsPath}/{uuid}", null, ct);
        }
        catch (SaasDomainsException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone is fine: the goal is that it doesn't exist.
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new SaasDomainsException(response.StatusCode, await ReadErrorAsync(response, ct));
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
    }

    private static async Task<ApiError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiError>(Json, ct);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return null; // 401s have an empty body
        }
    }
}

public sealed record ProviderDomain(string Uuid, string Host, string Status);

public sealed record DnsVerification(string DnsStatus);

// 400s come with {"error": "...", "error_code": "host_taken"}.
public sealed record ApiError(string? Error, string? ErrorCode);

public sealed class SaasDomainsException(HttpStatusCode status, ApiError? error)
    : Exception($"SaaS Custom Domains API {(int)status}: {error?.Error}")
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? ErrorCode { get; } = error?.ErrorCode;
}
