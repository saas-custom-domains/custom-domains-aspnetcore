using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bookwell.CustomDomains;

// A small client for the SaaS Custom Domains API, as a typed
// HttpClient. Docs: https://docs.saascustomdomains.com/custom-domains
public sealed class SaasDomainsClient(
    HttpClient http, IConfiguration config)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull,
        };

    private string DomainsPath =>
        $"api/v1/accounts/{config["SCD_ACCOUNT_UUID"]}" +
        $"/upstreams/{config["SCD_UPSTREAM_UUID"]}/custom_domains";

    public Task<ProviderDomain> CreateDomainAsync(
        string host, string? instructionsRecipient) =>
        SendAsync<ProviderDomain>(HttpMethod.Post, DomainsPath, new
        {
            host,
            // We email this address the exact DNS records to add.
            instructions_recipient = instructionsRecipient,
        });

    public Task<DnsVerification> VerifyDnsAsync(string uuid) =>
        SendAsync<DnsVerification>(HttpMethod.Post,
            $"{DomainsPath}/{uuid}/verify_dns_records");

    public async Task DeleteDomainAsync(string uuid)
    {
        try
        {
            await SendAsync<JsonElement>(HttpMethod.Delete,
                $"{DomainsPath}/{uuid}");
        }
        catch (SaasDomainsException e)
            when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone is fine: the goal is that it doesn't exist.
        }
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new SaasDomainsException(
                response.StatusCode, await ReadErrorAsync(response));
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<ApiError?> ReadErrorAsync(
        HttpResponseMessage response)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<ApiError>(Json);
        }
        catch (Exception e)
            when (e is JsonException or NotSupportedException)
        {
            return null; // 401s have an empty body
        }
    }
}

public sealed record ProviderDomain(
    string Uuid, string Host, string Status);

public sealed record DnsVerification(string DnsStatus);

// 400s come with {"error": "...", "error_code": "host_taken"}.
public sealed record ApiError(string? Error, string? ErrorCode);

public sealed class SaasDomainsException(
    HttpStatusCode status, ApiError? error)
    : Exception($"SaaS Custom Domains API {(int)status}: "
        + error?.Error)
{
    public HttpStatusCode StatusCode { get; } = status;
    public string? ErrorCode { get; } = error?.ErrorCode;
}
