using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Threading.RateLimiting;
using AfterTheCredits.Data;
using AfterTheCredits.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Access");
    options.Conventions.AllowAnonymousToPage("/Privacy");
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Access";
        options.Cookie.Name = "__Host-AfterTheCredits.Access";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = context =>
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var version = DemoAccessPassword.GetVersion(configuration);
            if (version is null || context.Principal?.FindFirst(DemoAccessPassword.VersionClaim)?.Value != version)
            {
                context.RejectPrincipal();
            }

            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var page = context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>();
        if (!HttpMethods.IsPost(context.Request.Method) || page?.ViewEnginePath != "/Access")
        {
            return RateLimitPartition.GetNoLimiter("other");
        }

        var key = "access:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsync(
            "Too many password attempts. Please try again in a few minutes.", cancellationToken);
    };
});
builder.Services.AddMemoryCache();
//builder.Services.AddDataProtection().SetApplicationName("AfterTheCredits");
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("AfterTheCredits");

var keysPath = builder.Configuration["DataProtection:KeysPath"];

if (!string.IsNullOrWhiteSpace(keysPath))
{
    Directory.CreateDirectory(keysPath);
    dataProtection.PersistKeysToFileSystem(
        new DirectoryInfo(keysPath));
}
builder.Services.AddScoped<DemoVisitorSession>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<GeminiEmbeddingService>();
builder.Services.AddScoped<RecommendationService>();
/*builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=afterthecredits.db;Foreign Keys=True"));*/
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=afterthecredits.db;Foreign Keys=True";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>() is not null)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await context.RequestServices.GetRequiredService<DemoVisitorSession>().InitializeAsync(
                context,
                context.RequestServices.GetRequiredService<AppDbContext>(),
                context.RequestServices.GetRequiredService<IDataProtectionProvider>(),
                app.Environment);
        }
    }

    await next(context);
});

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
