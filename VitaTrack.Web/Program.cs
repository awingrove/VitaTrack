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
app.UseStaticFiles();               // serves wwwroot (Bootstrap, HTMX, etc.)
app.UseRouting();
app.UseAuthorization();

// Conventional route: /Home, /Family, /Supplement, etc.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();