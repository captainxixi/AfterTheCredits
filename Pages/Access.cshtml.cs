using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AfterTheCredits.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AfterTheCredits.Pages;

public sealed class AccessModel(IConfiguration configuration) : PageModel
{
    [BindProperty]
    [Required]
    public string? Password { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        ReturnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null;
        return User.Identity?.IsAuthenticated == true ? ContinueToDemo() : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var version = DemoAccessPassword.GetVersion(configuration);
        if (!ModelState.IsValid || !DemoAccessPassword.Matches(Password, version))
        {
            Password = null;
            ModelState.Remove(nameof(Password));
            ModelState.AddModelError(string.Empty, "Incorrect password or access is unavailable.");
            return Page();
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "Invited visitor"),
                new Claim(DemoAccessPassword.VersionClaim, version!)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });

        return ContinueToDemo();
    }

    private IActionResult ContinueToDemo() =>
        Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl!) : RedirectToPage("/Index");
}
