using VitaTrack.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddInfra(builder.Configuration);

var app = builder.Build();

app.Services.InitDb();

// Standard middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
        "img-src 'self' data: https:; " +
        "connect-src 'self'; " +
        "font-src 'self';");
    await next();
});

app.UseStaticFiles();               // serves wwwroot (Bootstrap, HTMX, etc.)
app.UseRouting();
app.UseAuthorization();

// Conventional route: /Home, /Family, /Supplement, etc.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();