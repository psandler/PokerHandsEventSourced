using JasperFx.Events;
using Microsoft.Data.SqlClient;
using Polecat;

namespace PokerEventSourced.Store;

/// <summary>
/// Builds the Polecat document store and makes sure its database exists.
/// Polecat creates its own tables on first use, but not the database itself.
/// </summary>
public static class PokerStore
{
    public static DocumentStore Create(string connectionString, Action<StoreOptions>? configure = null) =>
        DocumentStore.For(options =>
        {
            options.Connection(connectionString);

            // One stream per hand, keyed like "phh:PokerStars:59937793578".
            options.Events.StreamIdentity = StreamIdentity.AsString;

            configure?.Invoke(options);
        });

    /// <summary>Creates the connection string's database if it doesn't exist yet.</summary>
    public static async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new ArgumentException("The connection string has no database name.", nameof(connectionString));
        }

        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "IF DB_ID(@name) IS NULL EXEC('CREATE DATABASE ' + @quoted)";
        command.Parameters.AddWithValue("@name", database);
        command.Parameters.AddWithValue("@quoted", QuoteName(database));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";
}
