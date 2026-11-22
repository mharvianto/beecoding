using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The real app on a throw-away SQLite file. The connection string travels in a process-wide env var,
/// so every test class that hosts the app belongs to the "app" collection and runs one at a time.
/// With <c>BEECODING_TEST_PG</c> set (an Npgsql connection string WITHOUT a database, e.g.
/// <c>Host=127.0.0.1;Port=5433;Username=postgres</c>) the same suite runs on PostgreSQL instead: each app gets a database of
/// its own, dropped afterwards.</summary>
[CollectionDefinition("app", DisableParallelization = true)]
public sealed class AppCollection { }

public sealed class TestApp : WebApplicationFactory<Program>
{
    /// <summary>The SQLite file path, or the Postgres database name.</summary>
    public string Db { get; }

    /// <summary>The server connection string when the suite runs on Postgres, else null.</summary>
    public static string? Pg { get; } = Environment.GetEnvironmentVariable("BEECODING_TEST_PG") is { Length: > 0 } s ? s : null;

    private readonly bool _ownsDb;

    public TestApp() : this(null) { }

    /// <summary>Pass a path (a database name on Postgres) to start the app again on a database that must outlive this instance (the caller deletes it).</summary>
    internal TestApp(string? db)
    {
        _ownsDb = db is null;
        if (Pg is null)
        {
            Db = db ?? Path.Combine(Path.GetTempPath(), $"beecoding-test-{Guid.NewGuid():N}.db");
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={Db}");
            Environment.SetEnvironmentVariable("Database__Provider", null);
            return;
        }
        Db = db ?? $"bt_{Guid.NewGuid():N}";
        if (_ownsDb) Exec($"CREATE DATABASE \"{Db}\"");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"{Pg};Database={Db};Maximum Pool Size=20");
        Environment.SetEnvironmentVariable("Database__Provider", "postgres");
    }

    private static void Exec(string sql)
    {
        using var c = new NpgsqlConnection(Pg + ";Database=postgres");
        c.Open();
        using var cmd = new NpgsqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !_ownsDb) return;
        if (Pg is not null)
        {
            NpgsqlConnection.ClearAllPools();
            try { Exec($"DROP DATABASE IF EXISTS \"{Db}\" WITH (FORCE)"); } catch { /* best effort */ }
            return;
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { Db, Db + "-wal", Db + "-shm" })
            try { File.Delete(f); } catch { /* still open or already gone */ }
    }
}
