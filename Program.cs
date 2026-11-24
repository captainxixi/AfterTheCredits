using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AfterTheCredits.Data;
using AfterTheCredits.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
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

app.Use(async (context, next) =>
{
    if (context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>() is not null)
    {
        context.Response.Headers.CacheControl = "no-store";
        await context.RequestServices.GetRequiredService<DemoVisitorSession>().InitializeAsync(
            context,
            context.RequestServices.GetRequiredService<AppDbContext>(),
            context.RequestServices.GetRequiredService<IDataProtectionProvider>(),
            app.Environment);
    }

    await next(context);
});

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
