using BeeCoding.Data;
using BeeCoding.Models;
using BeeCoding.Services.DataMove;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BeeCoding.Tests;

[Collection("app")]
public class MoveDataTests
{
    [Fact]
    public async Task move_data_copies_a_sqlite_file_into_an_empty_postgres_and_the_ids_continue_after_it()
    {
        if (TestApp.Pg is null) return;   // needs a server: runs with BEECODING_TEST_PG set (CI does)

        var file = Path.Combine(Path.GetTempPath(), $"beecoding-move-{Guid.NewGuid():N}.db");
        var target = $"bt_move_{Guid.NewGuid():N}";
        var admin = TestApp.Pg + ";Database=postgres";
        async Task Sql(string cs, string sql) { await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
        try
        {
            using (var sqlite = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file}").Options))
            {
                await sqlite.Database.MigrateAsync();
                var owner = new User { Email = "o@move.dev", DisplayName = "Owner", CreatedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc) };
                sqlite.Users.AddRange(owner, new User { Email = "k@move.dev", DisplayName = "Kid" });
                await sqlite.SaveChangesAsync();
                sqlite.BankProblems.Add(new BankProblem
                {
                    OwnerId = owner.Id, Title = "T", StatementMarkdown = "café ☃", IsPublic = true, Level = ProblemLevel.Medium,
                    TestCases = [new() { Stdin = "1\n", ExpectedStdout = "1\n", IsSample = true }, new() { Stdin = "2\n", ExpectedStdout = "2\n" }],
                });
                await sqlite.SaveChangesAsync();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            await Sql(admin, $"CREATE DATABASE \"{target}\"");
            var cs = $"{TestApp.Pg};Database={target}";
            var log = new StringWriter();
            Assert.Equal(0, await SqliteToPostgres.RunAsync(["move-data", file, cs], log));
            Assert.Contains("OK:", log.ToString());

            await using var pg = new NpgsqlConnection(cs);
            await pg.OpenAsync();
            async Task<object?> One(string sql) { await using var cmd = new NpgsqlCommand(sql, pg); return await cmd.ExecuteScalarAsync(); }
            Assert.Equal(2L, await One("SELECT COUNT(*) FROM \"Users\""));
            Assert.Equal(2L, await One("SELECT COUNT(*) FROM \"BankTestCases\""));
            Assert.Equal(true, await One("SELECT \"IsPublic\" FROM \"BankProblems\""));
            Assert.Equal("café ☃", await One("SELECT \"StatementMarkdown\" FROM \"BankProblems\""));
            Assert.Equal(new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc), await One("SELECT \"CreatedAt\" FROM \"Users\" WHERE \"Email\" = 'o@move.dev'"));
            Assert.True(System.Convert.ToInt64(await One("SELECT nextval(pg_get_serial_sequence('\"Users\"', 'Id'))")) > 2);   // ids continue after the copied ones

            // a second run refuses: the target is no longer empty
            Assert.Equal(2, await SqliteToPostgres.RunAsync(["move-data", file, cs], new StringWriter()));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try { await Sql(admin, $"DROP DATABASE IF EXISTS \"{target}\" WITH (FORCE)"); } catch { /* best effort */ }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { file, file + "-wal", file + "-shm" }) try { File.Delete(f); } catch { /* gone */ }
        }
    }
}
