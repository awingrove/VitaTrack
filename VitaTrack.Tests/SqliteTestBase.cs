using System.Data;
using Microsoft.Data.Sqlite;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.ServiceConnections;

namespace VitaTrack.Tests;

public abstract class SqliteTestBase : IDisposable
{
    protected readonly IDbConnection Connection;

    protected SqliteTestBase()
    {
        // In-memory SQLite – each test gets its own isolated DB
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        Connection = conn;

        // Create the schema exactly like the app does, but without seeding test data
        DbInit.EnsureCreated(Connection, seedData: false);
        Repository = new ServiceConnectionRepository(Connection);
    }

    protected ServiceConnectionRepository Repository { get; }

    public void Dispose()
    {
        Connection?.Close();
        Connection?.Dispose();
    }
}
