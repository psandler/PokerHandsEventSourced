using Microsoft.Data.SqlClient;
using PokerEventSourced.Store;

namespace PokerEventSourced.Tests.TestSupport;

/// <summary>
/// A throwaway SQL Server database with a unique name, dropped on dispose.
/// Skips the test when SQL Server isn't reachable.
/// Server: the POKER_TEST_SQLSERVER env var, or the local default instance with Windows auth.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private const string DefaultServer = "Server=localhost;Integrated Security=True;TrustServerCertificate=True";

    private TestDatabase(string connectionString, string name)
    {
        ConnectionString = connectionString;
        Name = name;
    }

    public string ConnectionString { get; }

    public string Name { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable("POKER_TEST_SQLSERVER") ?? DefaultServer;
        var name = $"PokerEventSourced_Test_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(server) { InitialCatalog = name, ConnectTimeout = 5 };

        try
        {
            await PokerStore.EnsureDatabaseExistsAsync(builder.ConnectionString, TestContext.Current.CancellationToken);
        }
        catch (SqlException ex)
        {
            Assert.Skip($"SQL Server not available: {ex.Message}");
        }

        return new TestDatabase(builder.ConnectionString, name);
    }

    public async ValueTask DisposeAsync()
    {
        // Only ever drops the database this class created.
        SqlConnection.ClearAllPools();
        var builder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        var quoted = PokerStore.QuoteName(Name);
        command.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
        await command.ExecuteNonQueryAsync();
    }
}
