using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VitaTrack.Core;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Features.Family;
using VitaTrack.Core.Features.Reporting;
using VitaTrack.Core.Features.LlmEnrichment;

using VitaTrack.Core.Features.Nutrients;
using VitaTrack.Core.Features.ServiceConnections;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Tests;

[TestClass]
public class ServiceCollectionExtensionsTests
{
    private const string FileDataSource = "Data Source=cov-audit.db";
    private const string MemoryDataSource = "Data Source=covtest;Mode=Memory;Cache=Shared";

    /// <summary>Builds a provider exactly like the app composes it, from configuration
    /// alone. There is no connection state to supply: the named clients carry none, and
    /// every value a request needs travels with the request.</summary>
    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
            })
            .Build();

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
        // Resolved, not merely registered. `AddHttpClient<IServiceCatalogClient,
        // ServiceCatalogClient>()` compiles and fails at resolution — the typed-client
        // factory demands a constructor taking HttpClient, and this one takes an
        // IHttpClientFactory — so nothing but an actual resolve catches that mistake.
        var catalogClient = sp.GetRequiredService<IServiceCatalogClient>();
        var probeHandler = sp.GetRequiredService<ProbeConnectionHandler>();
        var comparisonHandler = sp.GetRequiredService<BuildSupplementComparisonHandler>();

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
        Assert.IsInstanceOfType(catalogClient, typeof(ServiceCatalogClient));
        Assert.IsNotNull(probeHandler);
        Assert.IsNotNull(comparisonHandler);

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

        // The catalog client must see the connection the user just saved, not one
        // captured when the pooled handler was built.
        Assert.AreSame(catalogClient, sp.GetRequiredService<IServiceCatalogClient>());

        // One session id per process, shared with the descriptor the probe reads.
        var sessionId = sp.GetRequiredService<LlmSessionId>();
        Assert.AreSame(sessionId, sp.GetRequiredService<LlmSessionId>(),
            "a second id would leave the x-opencode-session header correlating nothing");
        Assert.AreEqual(ServiceDescriptorRegistry.SessionId, sessionId.Value,
            "the session id is the registry's, not a second one minted beside it");
    }

    /// <summary>The named client's pooled instance carries neither a destination nor a
    /// credential. Both are per-request values that belong to the connection being
    /// called (<see cref="LlmClientRequestTests"/>), and a default left here would be a
    /// second, silent source of both: a stale <c>BaseAddress</c> would resolve a
    /// relative URI against whatever connection used the pool last, and a default
    /// <c>Authorization</c> is copied onto any request that does not carry its own —
    /// so the credential one caller saved would ride along on another's call.
    /// <para>
    /// These three assertions pin two <em>values</em>, not the absence of a configure
    /// delegate: one that set a <c>User-Agent</c> or a retry policy would pass all three.
    /// </para>
    /// <para>
    /// What they do not pin is the registration itself, and nothing here can.
    /// <c>IHttpClientFactory.CreateClient</c> on an unknown name returns a fresh,
    /// unconfigured client and does not throw, so deleting the
    /// <c>AddHttpClient("llm", …)</c> line leaves this test green. The one assertion that
    /// does catch that is <see cref="AddCore_LlmClient_HasTheAppOwnedTimeout"/>, because
    /// an unregistered name comes back with the framework's default wait — which is the
    /// whole of the difference, not a symptom of a larger one: the handler cache is
    /// keyed by name whether or not the name was ever registered, so losing the
    /// registration costs that timeout and nothing else, silently rather than loudly.
    /// </para></summary>
    [TestMethod]
    public void AddCore_LlmClient_CarriesNoBaseAddressAndNoAuthorization()
    {
        using var provider = BuildProvider(MemoryDataSource);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("llm");

        Assert.IsNull(
            client.BaseAddress,
            "No BaseAddress: the request URI is absolute, so nothing on the client can redirect it.");
        Assert.IsNull(
            client.DefaultRequestHeaders.Authorization,
            "No default Authorization: the credential belongs to the connection being called, and a pooled "
            + "default is copied onto any request that does not set its own.");
        Assert.IsFalse(
            client.DefaultRequestHeaders.Contains("Authorization"),
            "Checked by name as well as by parsed value: a header .NET did not parse into Authorization "
            + "would still be copied onto the request.");
    }

    /// <summary>The named client's wait is the app's rather than the framework's.
    /// <c>HttpClient.Timeout</c> defaults to 100 seconds, an unregistered name comes
    /// back with exactly that default, and nothing else in the suite would notice the
    /// registration going — so this is the assertion standing in for it.
    /// <para>
    /// Asserted against the constant so the pin and the source cannot drift apart, and
    /// against the literal so that moving the constant is something argued for here
    /// rather than something inherited silently.
    /// </para></summary>
    [TestMethod]
    public void AddCore_LlmClient_HasTheAppOwnedTimeout()
    {
        using var provider = BuildProvider(MemoryDataSource);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("llm");

        Assert.AreEqual(
            120,
            ServiceCollectionExtensions.LlmTimeoutSeconds,
            "A non-streaming completion carrying up to 16k tokens has to finish inside the wait, and the "
            + "enrichment e2e spec budgets 180s for the same call. Move this deliberately, not by accident.");
        Assert.AreEqual(
            TimeSpan.FromSeconds(ServiceCollectionExtensions.LlmTimeoutSeconds),
            client.Timeout,
            "The 'llm' client must carry the app-owned timeout rather than HttpClient's 100s default, which "
            + "is also what an unregistered name would silently get.");
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
