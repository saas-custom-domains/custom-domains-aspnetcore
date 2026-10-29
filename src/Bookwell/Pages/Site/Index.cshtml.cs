using System.ComponentModel.DataAnnotations;
using Bookwell.CustomDomains;
using Bookwell.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bookwell.Pages.Site;

// A studio's booking page, on the studio's own domain.
[TenantSite]
public class SiteModel(AppDbContext db) : PageModel
{
    public static readonly string[] Times =
        ["09:00", "10:30", "14:00", "16:30"];

    public Studio Studio { get; private set; } = null!;

    [BindProperty]
    public BookingForm Form { get; set; } = new();

    public async Task OnGetAsync()
    {
        Studio = await LoadStudioAsync();
        Form.Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
    }

    // Razor Pages checks the antiforgery token before this runs.
    public async Task<IActionResult> OnPostAsync()
    {
        // The studio comes from the domain, never from the form.
        Studio = await LoadStudioAsync();

        if (!Studio.Services.Contains(Form.Service))
            ModelState.AddModelError("Form.Service", "Pick a service.");
        if (!Times.Contains(Form.Time))
            ModelState.AddModelError("Form.Time", "Pick a time.");
        if (Form.Date < DateOnly.FromDateTime(DateTime.Today))
            ModelState.AddModelError("Form.Date", "Pick a later day.");
        if (!ModelState.IsValid) return Page();

        var booking = new Booking
        {
            StudioId = Studio.Id,
            Service = Form.Service,
            StartsAt = Form.Date.ToDateTime(TimeOnly.Parse(Form.Time)),
            Name = Form.Name.Trim(),
            Email = Form.Email.Trim(),
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        // RedirectToPage() sends a relative Location ("/bookings/…"),
        // so the browser stays on the customer's domain.
        return RedirectToPage(
            "/Site/Booking", new { reference = booking.Reference });
    }

    private Task<Studio> LoadStudioAsync() =>
        db.Studios.SingleAsync(
            s => s.Id == HttpContext.GetTenant()!.StudioId);
}

public class BookingForm
{
    [Required]
    public string Name { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Service { get; set; } = "";

    [DataType(DataType.Date)]
    public DateOnly Date { get; set; }

    [Required]
    public string Time { get; set; } = "";
}
