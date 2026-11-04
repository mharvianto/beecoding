using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BeeCoding.Tests;

/// <summary>The real app on a throw-away SQLite file. The connection string travels in a process-wide env var,
/// so every test class that hosts the app belongs to the "app" collection and runs one at a time.</summary>
[CollectionDefinition("app", DisableParallelization = true)]
public sealed class AppCollection { }

public sealed class TestApp : WebApplicationFactory<Program>
{
    public string Db { get; }

    private readonly bool _ownsDb;

    public TestApp() : this(null) { }

    /// <summary>Pass a path to start the app again on a database that must outlive this instance (the caller deletes it).</summary>
    internal TestApp(string? db)
    {
        _ownsDb = db is null;
        Db = db ?? Path.Combine(Path.GetTempPath(), $"beecoding-test-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={Db}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !_ownsDb) return;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { Db, Db + "-wal", Db + "-shm" })
            try { File.Delete(f); } catch { /* still open or already gone */ }
    }
}
