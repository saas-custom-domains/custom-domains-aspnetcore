using System.Text.Json;
using Bookwell.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bookwell.CustomDomains;

public static class IntegrationEndpoints
{
    public static void MapCustomDomainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithMetadata(new AppHostOnlyAttribute());
        group.MapPost("/webhooks/saascustomdomains", HandleWebhook);
    }

    // Events: https://docs.saascustomdomains.com/webhooks
    private static async Task<IResult> HandleWebhook(
        HttpRequest request, AppDbContext db, IOptions<CustomDomainOptions> options)
    {
        // Check the token before reading the body.
        if (!Tokens.Matches(request.Query["token"], options.Value.WebhookToken))
        {
            return Results.Text("Unauthorized", statusCode: 401);
        }

        if (!request.HasJsonContentType()) return Results.BadRequest();
        Webhook? webhook;
        try
        {
            webhook = await request.ReadFromJsonAsync<Webhook>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var uuid = webhook?.Domain?.Uuid;
        var domain = uuid is null
            ? null
            : await db.CustomDomains.SingleOrDefaultAsync(d => d.ProviderUuid == uuid);
        // Not ours (or already removed): acknowledge so it isn't retried.
        if (domain is null) return Results.NoContent();

        switch (webhook!.Event)
        {
            case "domain_dns_proxy_record_valid":
                domain.Status = DomainStatus.Active;
                break;
            case "domain_dns_proxy_record_invalid":
                domain.Status = DomainStatus.Pending;
                break;
            case "tls_certificate_issued":
                domain.HttpsReady = true;
                break;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private sealed record Webhook(string? Event, WebhookDomain? Domain);

    private sealed record WebhookDomain(string? Uuid, string? Host);
}
