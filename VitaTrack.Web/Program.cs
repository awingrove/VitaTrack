using System.Data;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();

// Register a scoped SQLite connection (one per request)
// Connection string comes from appsettings.json, overridden by env vars (ConnectionStrings__Default)
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connStr = builder.Configuration.GetConnectionString("Default");
    return new System.Data.SQLite.SQLiteConnection(connStr);
});

// Register repositories (scoped)
builder.Services.AddScoped<IFamilyRepository, FamilyRepository>();
builder.Services.AddScoped<ISupplementRepository, SupplementRepository>();
builder.Services.AddScoped<ISupplementNutrientRepository, SupplementNutrientRepository>();
builder.Services.AddScoped<IPrescribedDoseRepository, PrescribedDoseRepository>();

// Register named HTTP clients for LLM service
builder.Services.AddHttpClient("openrouter", (sp, client) =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
    client.BaseAddress = new Uri(cfg["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg["OpenRouter:ApiKey"]}");
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient("scraper", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "VitaTrack/1.0 (supplement tracker)");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register LLM service
builder.Services.AddScoped<ILlmService, OpenRouterLlmService>();

var app = builder.Build();

// Ensure DB tables exist
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
    DbInit.EnsureCreated(db);
}

// Standard middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();               // serves wwwroot (Bootstrap, HTMX, etc.)
app.UseRouting();
app.UseAuthorization();

// Conventional route: /Home, /Family, /Supplement, etc.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();