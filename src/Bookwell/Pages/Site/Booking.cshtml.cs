using Bookwell.CustomDomains;
using Bookwell.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bookwell.Pages.Site;

[TenantSite]
public class BookingModel(AppDbContext db) : PageModel
{
    public Booking Booking { get; private set; } = null!;
    public string StudioName { get; private set; } = "";
    public string ManageUrl { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(Guid reference)
    {
        var tenant = HttpContext.GetTenant()!;
        var booking = await db.Bookings.SingleOrDefaultAsync(
            b => b.Reference == reference
                && b.StudioId == tenant.StudioId);
        if (booking is null) return NotFound();

        Booking = booking;
        StudioName = tenant.Name;
        // An absolute URL, the kind you'd put in a confirmation email.
        // PageLink() builds it from Request.Scheme and Request.Host,
        // which CustomDomainMiddleware set to the customer's domain.
        ManageUrl = Url.PageLink(
            "/Site/Booking", values: new { reference })!;
        return Page();
    }
}
