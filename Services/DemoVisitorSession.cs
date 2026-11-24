using System.Security.Cryptography;
using AfterTheCredits.Data;
using AfterTheCredits.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AfterTheCredits.Services;

public sealed class DemoVisitorSession
{
    private const string CookieName = "AfterTheCredits.Visitor";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public Guid? VisitorId { get; private set; }

    public async Task InitializeAsync(HttpContext context, AppDbContext db,
        IDataProtectionProvider protection, IWebHostEnvironment environment)
    {
        if (VisitorId is not null)
        {
            return;
        }

        var cancellationToken = context.RequestAborted;
        var now = DateTime.UtcNow;
        var protector = protection.CreateProtector("AfterTheCredits.DemoVisitor.v1");
        await db.DemoVisitors.Where(visitor => visitor.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        if (context.Request.Cookies.TryGetValue(CookieName, out var cookie) &&
            ReadVisitorId(protector, cookie) is Guid visitorId &&
            await db.DemoVisitors.AnyAsync(visitor =>
                visitor.Id == visitorId && visitor.ExpiresAtUtc > now, cancellationToken))
        {
            VisitorId = visitorId;
            return;
        }

        var visitor = new DemoVisitor
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Lifetime)
        };
        db.DemoVisitors.Add(visitor);
        await db.SaveChangesAsync(cancellationToken);

        VisitorId = visitor.Id;
        var options = GetCookieOptions(context, environment);
        options.Expires = new DateTimeOffset(visitor.ExpiresAtUtc);
        options.MaxAge = Lifetime;
        context.Response.Cookies.Append(CookieName, protector.Protect(visitor.Id.ToString("N")), options);
    }

    public async Task ResetAsync(HttpContext context, AppDbContext db, IWebHostEnvironment environment)
    {
        if (VisitorId is Guid visitorId)
        {
            await db.DemoVisitors.Where(visitor => visitor.Id == visitorId)
                .ExecuteDeleteAsync(context.RequestAborted);
        }

        VisitorId = null;
        context.Response.Cookies.Delete(CookieName, GetCookieOptions(context, environment));
    }

    private static Guid? ReadVisitorId(IDataProtector protector, string cookie)
    {
        try
        {
            return Guid.TryParseExact(protector.Unprotect(cookie), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static CookieOptions GetCookieOptions(HttpContext context, IWebHostEnvironment environment)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps || !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/"
        };
    }
}
