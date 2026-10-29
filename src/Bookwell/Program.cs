using System.Net.Http.Headers;
using Bookwell.CustomDomains;
using Bookwell.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var dataDir = builder.Configuration["DATA_DIR"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);

builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<AppDbContext>(db => db
    .UseSqlite($"Data Source={Path.Combine(dataDir, "app.db")}")
    .UseSeeding(AppDbContext.Seed));

// Antiforgery tokens are encrypted with these keys. Keep them across
// restarts (and share them between instances in production).
builder.Services.AddDataProtection().PersistKeysToFileSystem(
    new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddSingleton(services => Options.Create(
    CustomDomainOptions.FromConfiguration(
        services.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<MatcherPolicy, TenantEndpointPolicy>();
builder.Services.AddScoped<DomainService>();
builder.Services.AddHttpClient<SaasDomainsClient>((services, http) =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var apiUrl = config["SCD_API_URL"]
        ?? "https://app.saascustomdomains.com";
    http.BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/");
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue(
            "Bearer", config["SCD_API_TOKEN"]);
    http.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Database.EnsureCreated();
}

// Docker and load balancers call this by IP address, so it runs
// before the host check.
app.UseHealthChecks("/up");

app.UseMiddleware<CustomDomainMiddleware>();

// Explicitly after the middleware: routing needs the tenant to choose
// between the dashboard and the booking site (TenantEndpointPolicy).
// Without this line, WebApplication would run routing first.
app.UseRouting();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapCustomDomainEndpoints();

app.Run();

public partial class Program;
