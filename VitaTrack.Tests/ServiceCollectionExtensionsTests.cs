using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Features.Family;
using VitaTrack.Core.Features.Reporting;
using VitaTrack.Core.Services;

using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Tests;

[TestClass]
public class ServiceCollectionExtensionsTests
{
    private const string FileDataSource = "Data Source=cov-audit.db";
    private const string MemoryDataSource = "Data Source=covtest;Mode=Memory;Cache=Shared";

    /// <summary>Builds a provider exactly like the app composes it, except options are
    /// supplied directly (Program.cs owns the Configure&lt;VitaTrackOptions&gt; binding).</summary>
    private static ServiceProvider BuildProvider(
        string connectionString,
        string? baseUrl = "https://api.example.com/v1",
        string? apiKey = "key")
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
            })
            .Build();

        services.AddSingleton(Options.Create(new VitaTrackOptions
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
        }));
        services.AddCore(configuration);

        return services.BuildServiceProvider();
    }

    [TestMethod]
    public void AddCore_FileDataSource_IsRootedAgainstBaseDirectory()
    {
        using var provider = BuildProvider(FileDataSource);
        using var scope = provider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var sqlite = (SqliteConnection)db;

        Assert.IsTrue(
            sqlite.DataSource.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal),
            $"DataSource '{sqlite.DataSource}' must be rooted under '{AppContext.BaseDirectory}'.");
        Assert.IsTrue(
            sqlite.DataSource.EndsWith("cov-audit.db", StringComparison.Ordinal),
            $"DataSource '{sqlite.DataSource}' must keep the configured file name.");
        Assert.AreNotEqual("cov-audit.db", sqlite.DataSource, "Relative data source must have been rewritten.");

        // File mode has no shared-memory keep-alive connection.
        Assert.IsNull(provider.GetService<SqliteConnection>(), "File mode must not register a keep-alive SqliteConnection.");
    }

    [TestMethod]
    public void AddCore_MemoryMode_RegistersKeepAliveAndSharedDb()
    {
        using var provider = BuildProvider(MemoryDataSource);

        var keepAlive = provider.GetService<SqliteConnection>();
        Assert.IsNotNull(keepAlive, "Memory mode must register a keep-alive SqliteConnection singleton.");
        Assert.AreEqual(ConnectionState.Open, keepAlive!.State, "Keep-alive connection must be open.");

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
            Assert.IsInstanceOfType(db, typeof(SqliteConnection));
            var sqlite = (SqliteConnection)db;
            Assert.AreEqual(ConnectionState.Closed, sqlite.State, "Scoped connection is opened lazily by consumers.");
        }

        // Disposing the scope must not have closed the singleton keep-alive.
        Assert.AreEqual(ConnectionState.Open, keepAlive.State);
    }

    [TestMethod]
    public void AddCore_RegistersAllRepositoriesAndServices()
    {
        using var provider = BuildProvider(FileDataSource);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var familyRepo = sp.GetRequiredService<IFamilyRepository>();
        var supplementRepo = sp.GetRequiredService<ISupplementRepository>();
        var nutrientRepo = sp.GetRequiredService<ISupplementNutrientRepository>();
        var doseRepo = sp.GetRequiredService<IPrescribedDoseRepository>();
        var nutrientService = sp.GetRequiredService<ISupplementNutrientService>();
        var reportingService = sp.GetRequiredService<IReportingService>();
        var scraperService = sp.GetRequiredService<IHtmlScraperService>();
        var llmClient = sp.GetRequiredService<ILlmClient>();
        var labelParser = sp.GetRequiredService<ISupplementLabelParser>();
        var csvImport = sp.GetRequiredService<ICsvImportService>();
        var llmService = sp.GetRequiredService<ILlmService>();

        Assert.IsNotNull(familyRepo);
        Assert.IsNotNull(supplementRepo);
        Assert.IsNotNull(nutrientRepo);
        Assert.IsNotNull(doseRepo);
        Assert.IsNotNull(nutrientService);
        Assert.IsNotNull(reportingService);
        Assert.IsNotNull(scraperService);
        Assert.IsNotNull(llmClient);
        Assert.IsNotNull(labelParser);
        Assert.IsNotNull(csvImport);
        Assert.IsNotNull(llmService);

        // Each interface maps to its own concrete component.
        CollectionAssert.AllItemsAreUnique(
            new object[]
            {
                familyRepo, supplementRepo, nutrientRepo, doseRepo, nutrientService,
                reportingService, scraperService, llmClient, labelParser, csvImport, llmService,
            },
            "Every registered service must resolve to a distinct concrete instance.");

        // Scoped lifetime: same interface within one scope resolves to the same instance.
        Assert.AreSame(familyRepo, sp.GetRequiredService<IFamilyRepository>());
        Assert.AreSame(llmService, sp.GetRequiredService<ILlmService>());
    }

    [TestMethod]
    public void AddCore_LlmClient_HasBaseUrlAndAuthHeader()
    {
        using (var provider = BuildProvider(MemoryDataSource))
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("llm");

            Assert.AreEqual(new Uri("https://api.example.com/v1/"), client.BaseAddress, "Trailing slash must be appended.");
            Assert.AreEqual("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
            Assert.AreEqual("key", client.DefaultRequestHeaders.Authorization?.Parameter);
            Assert.AreEqual(TimeSpan.FromSeconds(120), client.Timeout);
        }

        // A BaseUrl without a trailing slash normalizes identically.
        using (var provider = BuildProvider(MemoryDataSource, baseUrl: "https://api.example.com/v1"))
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("llm");

            Assert.AreEqual(new Uri("https://api.example.com/v1/"), client.BaseAddress,
                "Base URL without trailing slash must normalize identically.");
        }

        // An empty BaseUrl leaves BaseAddress unset (but the auth header is still applied).
        using (var provider = BuildProvider(MemoryDataSource, baseUrl: ""))
        {
            var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("llm");

            Assert.IsNull(client.BaseAddress, "Empty BaseUrl must leave BaseAddress unset.");
            Assert.AreEqual("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
            Assert.AreEqual("key", client.DefaultRequestHeaders.Authorization?.Parameter);
        }
    }

    [TestMethod]
    public void AddCore_ScraperClient_HasUserAgentAndTimeout()
    {
        using var provider = BuildProvider(MemoryDataSource);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("scraper");

        Assert.AreEqual("VitaTrack/1.0 (supplement tracker)", client.DefaultRequestHeaders.UserAgent.ToString());
        Assert.AreEqual(TimeSpan.FromSeconds(30), client.Timeout);
    }

    [TestMethod]
    public void InitDb_CreatesSharedMemorySchema()
    {
        using var provider = BuildProvider(MemoryDataSource);

        provider.InitDb();

        // A brand-new scope (and thus a brand-new connection) must still see the schema:
        // the keep-alive singleton keeps the shared in-memory database alive across scopes.
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Supplements";
        var count = Convert.ToInt32(cmd.ExecuteScalar());

        Assert.AreEqual(3, count, "InitDb must create and seed the Supplements table in the shared memory database.");
    }
}
