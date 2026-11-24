using AfterTheCredits.Data;
using AfterTheCredits.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AfterTheCredits.Pages;

public class ResetDemoModel(
    DemoVisitorSession session, AppDbContext db, IWebHostEnvironment environment) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await session.ResetAsync(HttpContext, db, environment);
        return RedirectToPage("/Index");
    }
}
