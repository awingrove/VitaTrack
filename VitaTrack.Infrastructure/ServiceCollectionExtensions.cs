using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration.GetConnectionString("Default");
        var builder = new SqliteConnectionStringBuilder(connStr);

        // Resolve relative file paths against the application base directory so the database
        // file is created next to the executable, not in the current working directory.
        if (!string.IsNullOrWhiteSpace(builder.DataSource)
            && !Path.IsPathRooted(builder.DataSource)
            && !builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            && builder.Mode != SqliteOpenMode.Memory)
        {
            builder.DataSource = Path.Combine(AppContext.BaseDirectory, builder.DataSource);
        }

        var resolvedConnStr = builder.ConnectionString;

        // For shared in-memory databases, keep one connection open for the lifetime of the
        // application so the database is not destroyed when individual scopes are disposed.
        if (builder.Mode == SqliteOpenMode.Memory)
        {
            services.AddSingleton(_ =>
            {
                var keepAlive = new SqliteConnection(resolvedConnStr);
                keepAlive.Open();
                return keepAlive;
            });
        }

        services.AddScoped<IDbConnection>(_ => new SqliteConnection(resolvedConnStr));

        services.AddScoped<IFamilyRepository, FamilyRepository>();
        services.AddScoped<ISupplementRepository, SupplementRepository>();
        services.AddScoped<ISupplementNutrientRepository, SupplementNutrientRepository>();
        services.AddScoped<IPrescribedDoseRepository, PrescribedDoseRepository>();

        services.AddScoped<ISupplementNutrientService, SupplementNutrientService>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<IHtmlScraperService, HtmlScraperService>();
        services.AddScoped<ILlmClient, LlmClient>();
        services.AddScoped<ISupplementLabelParser, SupplementLabelParser>();

        services.AddHttpClient("llm", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<VitaTrackOptions>>().Value;
            var baseUrl = options.BaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                if (!baseUrl.EndsWith('/'))
                    baseUrl += '/';
                client.BaseAddress = new Uri(baseUrl);
            }
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddHttpClient("scraper", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "VitaTrack/1.0 (supplement tracker)");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<ILlmService, LlmService>();

        return services;
    }

    public static void InitDb(this IServiceProvider services)
    {
        // For shared in-memory databases, ensure the keep-alive connection is opened first
        // so the database is not destroyed when the initialization scope is disposed.
        var keepAlive = services.GetService<Microsoft.Data.Sqlite.SqliteConnection>();
        keepAlive?.Open();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        DbInit.EnsureCreated(db);
    }
}
