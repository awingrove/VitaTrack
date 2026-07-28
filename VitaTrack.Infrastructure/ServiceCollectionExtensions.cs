using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Services;

namespace VitaTrack.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IDbConnection>(sp =>
            {
                var connStr = configuration.GetConnectionString("Default");
                return new SqliteConnection(connStr);
            });

            services.AddScoped<IFamilyRepository, FamilyRepository>();
            services.AddScoped<ISupplementRepository, SupplementRepository>();
            services.AddScoped<ISupplementNutrientRepository, SupplementNutrientRepository>();
            services.AddScoped<IPrescribedDoseRepository, PrescribedDoseRepository>();

            services.AddHttpClient("openrouter", (sp, client) =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                client.BaseAddress = new Uri(cfg["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg["OpenRouter:ApiKey"]}");
                client.Timeout = TimeSpan.FromSeconds(120);
            });

            services.AddHttpClient("scraper", client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", "VitaTrack/1.0 (supplement tracker)");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddScoped<ILlmService, OpenRouterLlmService>();

            return services;
        }

        public static void InitDb(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
            DbInit.EnsureCreated(db);
        }
    }
}
