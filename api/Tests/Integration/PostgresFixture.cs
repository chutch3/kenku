using Npgsql;

namespace API.Tests.Integration;

/// <summary>
/// Manages per-test Postgres database lifecycle. Each test creates an isolated database
/// (kenku_test_&lt;guid&gt;) and drops it in teardown. Requires postgres running via docker-compose.test.yml.
/// Set TEST_POSTGRES_CONNECTION_STRING to override the default localhost:5433 endpoint.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
        ?? "Host=localhost;Port=5433;Username=kenku;Password=kenku_test;Database=postgres";

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<bool> IsReachableAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(AdminConnectionString);
            await conn.OpenAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token);
            return true;
        }
        catch { return false; }
    }

    public async Task<string> CreateDatabaseAsync()
    {
        string name = $"kenku_test_{Guid.NewGuid():N}";
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{name}\"";
        await cmd.ExecuteNonQueryAsync();
        return name;
    }

    public async Task DropDatabaseAsync(string name)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var kill = conn.CreateCommand();
        kill.CommandText = $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{name}'";
        await kill.ExecuteNonQueryAsync();
        await using var drop = conn.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\"";
        await drop.ExecuteNonQueryAsync();
    }

    public string GetConnectionString(string dbName)
    {
        var b = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = dbName };
        return b.ToString();
    }
}
