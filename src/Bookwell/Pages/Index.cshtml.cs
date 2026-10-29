using Bookwell.CustomDomains;
using Bookwell.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bookwell.Pages;

// The dashboard. This example has no login: in your app, check in every
// handler that the signed-in user owns the studio and the domain.
[AppHostOnly]
public class IndexModel(AppDbContext db, DomainService domains) : PageModel
{
    public List<Studio> Studios { get; private set; } = [];
    public int? ErrorStudioId { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => Studios = await LoadStudiosAsync();

    public async Task<IActionResult> OnPostAddAsync(int studioId, string? host, string? dnsContact)
    {
        if (!await db.Studios.AnyAsync(s => s.Id == studioId)) return NotFound();

        Error = await domains.AddAsync(
            studioId, host ?? "", string.IsNullOrWhiteSpace(dnsContact) ? null : dnsContact.Trim());
        if (Error is null) return RedirectToPage();

        ErrorStudioId = studioId;
        Studios = await LoadStudiosAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCheckAsync(int id)
    {
        if (await db.CustomDomains.FindAsync(id) is { } domain) await domains.CheckAsync(domain);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        if (await db.CustomDomains.FindAsync(id) is { } domain) await domains.RemoveAsync(domain);
        return RedirectToPage();
    }

    public static string StatusLabel(CustomDomain domain) => domain switch
    {
        { Status: DomainStatus.Active, HttpsReady: true } => "Live",
        { Status: DomainStatus.Active } => "DNS OK, waiting for HTTPS",
        _ => "Waiting for DNS",
    };

    private Task<List<Studio>> LoadStudiosAsync() =>
        db.Studios.Include(s => s.Domains.OrderBy(d => d.Id)).OrderBy(s => s.Id).ToListAsync();
}
