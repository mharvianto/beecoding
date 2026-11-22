using System.Globalization;
using BeeCoding.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using NpgsqlTypes;

namespace BeeCoding.Services.DataMove;

/// <summary>
/// One-off copy of a SQLite database into an empty PostgreSQL one:
/// <c>dotnet BeeCoding.dll move-data &lt;file.db&gt; "&lt;Npgsql connection string&gt;"</c>.
/// The values are copied RAW (no EF converters), so sealed secrets (<c>enc1:…</c>) arrive untouched — the target app must use the SAME
/// Data Protection keys as the source, or they will not open. The source is opened read-only and must already be at the current SQLite schema
/// (start the current app on it once). The target is migrated first and must hold no rows. Ends with a row-count and key-sum comparison per
/// table; the exit code is 1 on any difference.
/// </summary>
public static class SqliteToPostgres
{
    public static async Task<int> RunAsync(string[] args, TextWriter log)
    {
        if (args.Length != 3) { log.WriteLine("usage: move-data <sqlite file> \"<Npgsql connection string, with Database=>\""); return 2; }
        var sqlitePath = args[1];
        var pgConn = args[2];
        if (!File.Exists(sqlitePath)) { log.WriteLine($"no such file: {sqlitePath}"); return 2; }

        // 1. the source must be at the current schema
        var srcCs = new SqliteConnectionStringBuilder { DataSource = sqlitePath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using (var srcDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(srcCs).Options))
        {
            var pending = (await srcDb.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count > 0)
            {
                log.WriteLine($"the SQLite database is {pending.Count} migration(s) behind ({pending[0]} …). Start the current app on it once so it migrates, then run this again.");
                return 2;
            }
        }

        // 2. the target gets its schema from the Postgres migrations, and must be empty
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(pgConn, s => s.MigrationsAssembly("BeeCoding.Core.Postgres")).Options;
        using var tgt = new AppDbContext(opts);
        await tgt.Database.MigrateAsync();

        var tables = Plan(tgt.Model);
        await using var pg = new NpgsqlConnection(pgConn);
        await pg.OpenAsync();
        foreach (var t in tables)
            if (await ScalarLongAsync(pg, $"SELECT COUNT(*) FROM \"{t.Table}\"") > 0)
            { log.WriteLine($"the target is not empty (\"{t.Table}\" has rows). Use a new, empty database."); return 2; }

        await using var src = new SqliteConnection(srcCs);
        await src.OpenAsync();

        // Foreign keys are satisfied by the copy order; self-references and cycles need the constraints off for the session (needs a superuser).
        var replica = false;
        try { await ExecAsync(pg, "SET session_replication_role = replica"); replica = true; }
        catch (PostgresException) { log.WriteLine("note: cannot switch constraint checks off (not a superuser); copying in dependency order only."); }

        int nulBytes = 0;
        foreach (var t in tables)
        {
            var cols = string.Join(", ", t.Columns.Select(c => $"\"{c.Name}\""));
            long n = 0;
            using (var cmd = src.CreateCommand())
            {
                cmd.CommandText = $"SELECT {cols} FROM \"{t.Table}\"";
                using var rd = await cmd.ExecuteReaderAsync();
                await using var w = await pg.BeginBinaryImportAsync($"COPY \"{t.Table}\" ({cols}) FROM STDIN (FORMAT BINARY)");
                while (await rd.ReadAsync())
                {
                    await w.StartRowAsync();
                    for (int i = 0; i < t.Columns.Count; i++)
                    {
                        var c = t.Columns[i];
                        var v = rd.IsDBNull(i) ? null : Convert(rd.GetValue(i), c.Type, ref nulBytes);
                        if (v is null) { await w.WriteNullAsync(); continue; }
                        await WriteAsync(w, v);
                    }
                    n++;
                }
                await w.CompleteAsync();
            }
            log.WriteLine($"{t.Table}: {n}");
        }
        if (nulBytes > 0) log.WriteLine($"note: removed {nulBytes} NUL character(s) from text (Postgres cannot store them).");

        if (replica) await ExecAsync(pg, "SET session_replication_role = DEFAULT");

        // 3. identity sequences continue after the copied ids
        foreach (var t in tables.Where(t => t.IdentityColumn is not null))
            await ExecAsync(pg, $"SELECT setval(pg_get_serial_sequence('\"{t.Table}\"', '{t.IdentityColumn}'), COALESCE((SELECT MAX(\"{t.IdentityColumn}\") FROM \"{t.Table}\"), 0) + 1, false)");

        // 4. compare
        int bad = 0;
        foreach (var t in tables)
        {
            var key = t.IdentityColumn ?? t.IntKey;
            var sql = key is null ? $"SELECT COUNT(*), 0 FROM \"{t.Table}\"" : $"SELECT COUNT(*), COALESCE(SUM(\"{key}\"), 0) FROM \"{t.Table}\"";
            var (sc, ss) = await PairAsync(src, sql);
            var (pc, ps) = await PairAsync(pg, sql);
            if (sc != pc || ss != ps) { bad++; log.WriteLine($"MISMATCH {t.Table}: source {sc} rows / key sum {ss}, target {pc} / {ps}"); }
        }
        log.WriteLine(bad == 0 ? $"OK: {tables.Count} tables copied and compared." : $"{bad} table(s) differ.");
        log.WriteLine("Reminder: run the app on Postgres with the SAME Data Protection keys as before, or sealed secrets (2-step codes, AI keys, LTI keys) will not open.");
        return bad == 0 ? 0 : 1;
    }

    private sealed record Col(string Name, Type Type);
    private sealed record TablePlan(string Table, List<Col> Columns, string? IdentityColumn, string? IntKey);

    /// <summary>Every mapped table, parents before children, with the type each column has in the database.</summary>
    private static List<TablePlan> Plan(IModel model)
    {
        var entities = model.GetEntityTypes().Where(e => e.GetTableName() is not null && e.FindOwnership() is null).ToList();
        if (entities.GroupBy(e => e.GetTableName()).Any(g => g.Count() > 1)) throw new NotSupportedException("tables shared by several entity types");
        var done = new HashSet<IEntityType>();
        var order = new List<IEntityType>();
        void Visit(IEntityType e)
        {
            if (!done.Add(e)) return;
            foreach (var fk in e.GetForeignKeys()) if (fk.PrincipalEntityType != e && entities.Contains(fk.PrincipalEntityType)) Visit(fk.PrincipalEntityType);
            order.Add(e);
        }
        foreach (var e in entities.OrderBy(e => e.GetTableName())) Visit(e);

        return order.Select(e =>
        {
            var table = e.GetTableName()!;
            var id = StoreObjectIdentifier.Table(table, e.GetSchema());
            var props = e.GetProperties().Where(p => p.GetColumnName(id) is not null).ToList();
            var cols = props.Select(p =>
            {
                var t = p.GetValueConverter()?.ProviderClrType ?? p.GetTypeMapping().Converter?.ProviderClrType ?? p.ClrType;
                return new Col(p.GetColumnName(id)!, Nullable.GetUnderlyingType(t) ?? t);
            }).ToList();
            var pk = e.FindPrimaryKey()?.Properties;
            string? identity = null, intKey = null;
            if (pk is { Count: 1 } && (pk[0].ClrType == typeof(int) || pk[0].ClrType == typeof(long)))
            {
                intKey = pk[0].GetColumnName(id);
                if (pk[0].ValueGenerated == ValueGenerated.OnAdd) identity = intKey;
            }
            return new TablePlan(table, cols, identity, intKey);
        }).ToList();
    }

    /// <summary>SQLite hands back long / double / string / byte[]; turn it into what the Postgres column holds.</summary>
    private static object Convert(object v, Type to, ref int nul)
    {
        if (to == typeof(bool)) return System.Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0;
        if (to == typeof(int)) return System.Convert.ToInt32(v, CultureInfo.InvariantCulture);
        if (to == typeof(long)) return System.Convert.ToInt64(v, CultureInfo.InvariantCulture);
        if (to == typeof(short)) return System.Convert.ToInt16(v, CultureInfo.InvariantCulture);
        if (to == typeof(double)) return System.Convert.ToDouble(v, CultureInfo.InvariantCulture);
        if (to == typeof(float)) return System.Convert.ToSingle(v, CultureInfo.InvariantCulture);
        if (to == typeof(decimal)) return decimal.Parse(System.Convert.ToString(v, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        if (to == typeof(DateTime)) return DateTime.Parse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        if (to == typeof(DateOnly)) return DateOnly.Parse((string)v, CultureInfo.InvariantCulture);
        if (to == typeof(TimeSpan)) return TimeSpan.Parse((string)v, CultureInfo.InvariantCulture);
        if (to == typeof(Guid)) return v is byte[] b ? new Guid(b) : Guid.Parse((string)v);
        if (to == typeof(byte[])) return (byte[])v;
        if (to == typeof(string))
        {
            var s = System.Convert.ToString(v, CultureInfo.InvariantCulture)!;
            if (s.Contains('\0')) { nul += s.Count(ch => ch == '\0'); s = s.Replace("\0", ""); }
            return s;
        }
        throw new NotSupportedException($"column type {to.Name} is not handled by the copy");
    }

    private static Task WriteAsync(NpgsqlBinaryImporter w, object v) => v switch
    {
        bool x => w.WriteAsync(x, NpgsqlDbType.Boolean),
        int x => w.WriteAsync(x, NpgsqlDbType.Integer),
        long x => w.WriteAsync(x, NpgsqlDbType.Bigint),
        short x => w.WriteAsync(x, NpgsqlDbType.Smallint),
        double x => w.WriteAsync(x, NpgsqlDbType.Double),
        float x => w.WriteAsync(x, NpgsqlDbType.Real),
        decimal x => w.WriteAsync(x, NpgsqlDbType.Numeric),
        DateTime x => w.WriteAsync(x, NpgsqlDbType.TimestampTz),
        DateOnly x => w.WriteAsync(x, NpgsqlDbType.Date),
        TimeSpan x => w.WriteAsync(x, NpgsqlDbType.Interval),
        Guid x => w.WriteAsync(x, NpgsqlDbType.Uuid),
        byte[] x => w.WriteAsync(x, NpgsqlDbType.Bytea),
        string x => w.WriteAsync(x, NpgsqlDbType.Text),
        _ => throw new NotSupportedException(v.GetType().Name),
    };

    private static async Task ExecAsync(NpgsqlConnection c, string sql) { await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> ScalarLongAsync(NpgsqlConnection c, string sql) { await using var cmd = new NpgsqlCommand(sql, c); return System.Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static async Task<(long, long)> PairAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c); await using var r = await cmd.ExecuteReaderAsync(); await r.ReadAsync();
        return (r.GetInt64(0), System.Convert.ToInt64(r.GetValue(1)));
    }
    private static async Task<(long, long)> PairAsync(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = sql; using var r = await cmd.ExecuteReaderAsync(); await r.ReadAsync();
        return (r.GetInt64(0), System.Convert.ToInt64(r.GetValue(1)));
    }
}
